namespace WebCrawler.Services;

using OpenQA.Selenium;
using Serilog;

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
        Log.Information(
    "Starting crawl for {DomainName}",
    url);
        _driver.Navigate().GoToUrl(url);

    //    Log.Logger.LogInformation(
    //"Fetched {Url} in {ElapsedMs}ms",
    //url,
    //stopwatch.ElapsedMilliseconds);

        var html = _driver.PageSource;

        return Task.FromResult(
            new FetchedPage(
                url,
                html,
                _driver.Title));
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