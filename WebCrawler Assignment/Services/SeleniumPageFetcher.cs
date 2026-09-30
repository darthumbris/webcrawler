namespace WebCrawler.Services;

using OpenQA.Selenium;
using Serilog;
using System.Diagnostics;

public sealed record FetchedPage(
    string Url,
    string Html,
    string? Title);

public sealed class SeleniumPageFetcher
{
    private readonly IWebDriver _driver;

    public SeleniumPageFetcher(IWebDriver driver)
    {
        _driver = driver;
    }

    public Task<FetchedPage> FetchAsync(
        string url,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        _driver.Navigate().GoToUrl(url);

        //TODO maybe also handle stuff like scrolling?

        Log.Information(
    "Fetched {Url} in {ElapsedMs}ms",
    url,
    stopwatch.ElapsedMilliseconds);

        var html = _driver.PageSource;

        return Task.FromResult(
            new FetchedPage(
                url,
                html,
                _driver.Title));
    }

    public Task<FetchedPage> FetchRobot(string url)
    {
        _driver.Navigate().GoToUrl(url);
        var html = _driver.PageSource;
        return Task.FromResult(
            new FetchedPage(
                url,
                html,
                _driver.Title));
    }

    public void Quit()
    {
        _driver.Quit();
    }
}

//TODO need to implement: and they need to be configurable
//timeout,
//javascript execution, 
//total crawl duration,
//max pages,
//selenium failures,
//cancellation,
//driver lifetime

//TODO make grid url etc configurable