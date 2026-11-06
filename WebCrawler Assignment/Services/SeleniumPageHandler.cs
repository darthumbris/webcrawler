namespace WebCrawler.Services;

using OpenQA.Selenium;
using Serilog;
using WebCrawler.Configuration;
using WebCrawler.Models;

public sealed record FetchedPage(
    string Url,
    string Html,
    string? Title);

public sealed record FetchedSiteMap
(
    IReadOnlyCollection<Uri> Links,
    IReadOnlyCollection<Uri> SiteMaps,
    Uri Url
);

public sealed class SeleniumPageHandler
{
    //remote
    public SeleniumPageHandler(BrowserType browser, string hubUrl)
    {
        for (int i = 0; i < _requestOptions.MaxSimultanousRequests; i++)
        {
            var driver = WebcrawlerDriver.CreateGridInstance(browser, hubUrl);
            _executors.Add((IJavaScriptExecutor)driver);
            _drivers.Add(driver);
        }
    }

    //Local
    public SeleniumPageHandler(BrowserType browser)
    {
        for (int i = 0; i < _requestOptions.MaxSimultanousRequests; i++)
        {
            var driver = WebcrawlerDriver.CreateGridInstance(browser);
            _executors.Add((IJavaScriptExecutor)driver);
            _drivers.Add(driver);
        }
    }

    private readonly List<IWebDriver> _drivers = [];
    private readonly RequestOptions _requestOptions = new();
    private readonly List<IJavaScriptExecutor> _executors = [];

    public async Task<string> FetchPageAsync(
        string url,
        int driverIndex,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var driver = _drivers[driverIndex];
        driver.Navigate().GoToUrl(url);
        //TODO check if the issue with the pages not being fully loaded is now fixed with using more drivers

        return driver.PageSource;
    }

    public async Task<string> ExecuteJavascriptAsync(string javascript, int driverIndex, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return (string?)_executors[driverIndex].ExecuteAsyncScript(javascript) ?? string.Empty;
    }

    public async Task<string> ExecuteJavascript(string javascript, int driverIndex, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return (string?)_executors[driverIndex].ExecuteScript(javascript) ?? string.Empty;
    }

    public void Quit()
    {
        Log.Information("Closing Selenium WebDriver instance");
        foreach (var driver in _drivers)
        {
            driver.Quit();
        }
    }
}