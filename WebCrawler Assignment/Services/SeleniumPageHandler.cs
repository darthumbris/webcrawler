namespace WebCrawler.Services;

using OpenQA.Selenium;
using Serilog;
using WebCrawler.Configuration;
using WebCrawler.Models;

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
        Log.Information("Creating {Count} remote webdrivers for browser {BrowserType} to hub {Url}", _requestOptions.MaxSimultanousRequests, browser, hubUrl);
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
        Log.Information("Creating {Count} local webdrivers for browser {BrowserType}", _requestOptions.MaxSimultanousRequests, browser);
        for (int i = 0; i < _requestOptions.MaxSimultanousRequests; i++)
        {
            var driver = WebcrawlerDriver.CreateGridInstance(browser);
            _executors.Add((IJavaScriptExecutor)driver);
            _drivers.Add(driver);
        }
    }


    public SeleniumPageHandler(BrowserType browser, string hubUrl, int requests)
    {
        requests = Math.Min(requests, _requestOptions.MaxSimultanousRequests);
        Log.Information("Creating {Count} remote webdrivers for browser {BrowserType} to hub {Url}", requests, browser, hubUrl);
        for (int i = 0; i < requests; i++)
        {
            var driver = WebcrawlerDriver.CreateGridInstance(browser, hubUrl);
            _executors.Add((IJavaScriptExecutor)driver);
            _drivers.Add(driver);
        }
    }

    private readonly List<IWebDriver> _drivers = [];
    private readonly RequestOptions _requestOptions = new();
    private readonly List<IJavaScriptExecutor> _executors = [];

    private async Task ScrollPage(int driverIndex,
        CancellationToken cancellationToken)
    {
        var executor = _executors[driverIndex];
        var scrollsPerformed = 0;
        var lastHeight = executor.ExecuteScript("return document.body.scrollHeight");

        while (scrollsPerformed < _requestOptions.MaxScrolls)
        {
            executor.ExecuteScript("window.scrollTo(0, document.body.scrollHeight);");
            await Task.Delay(_requestOptions.ScrollDelay, cancellationToken);

            var newHeight = executor.ExecuteScript("return document.body.scrollHeight");
            if (newHeight != null && newHeight.Equals(lastHeight))
            {
                break;
            }
            lastHeight = newHeight;
            scrollsPerformed += 1;
        }
    }

    public async Task<string> FetchPageAsync(
        string url,
        int driverIndex,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var driver = _drivers[driverIndex];
        driver.Navigate().GoToUrl(url);
        await ScrollPage(driverIndex, cancellationToken);

        //really simple check for 429 Too many Requests
        //because Selenium doesn't give response codes for some stupid reason....
        if (driver.PageSource.Contains("<h1>Too Many Requests</h1>"))
        {
            throw new Exception("Too many request response detected");
        }

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
        Log.Information("Closing Selenium WebDriver instances");
        foreach (var driver in _drivers)
        {
            driver.Quit();
        }
    }
}