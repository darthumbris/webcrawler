namespace WebCrawler.Services;

using OpenQA.Selenium;
using Serilog;
using System.Diagnostics;
using System.Net;
using System.Xml;
using System.Xml.Linq;

public sealed record FetchedPage(
    string Url,
    string Html,
    string? Title);

public sealed record FetchedSiteMap
(
    IReadOnlyCollection<string> Links
);

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
        //TODO maybe have a timeout for the fetch?
        //use the cancellationToken for this
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

    public Task<FetchedPage> FetchRobot(string url, CancellationToken cancellationToken)
    {
        //TODO handle cancellationToken 
        Log.Information("Loading robots.txt from: {Url}", url);
        _driver.Navigate().GoToUrl(url);
        var html = _driver.PageSource;
        return Task.FromResult(
            new FetchedPage(
                url,
                html,
                _driver.Title));
    }

    public Task<FetchedSiteMap> FetchSiteMap(SiteMap siteMap, CancellationToken cancellationToken)
    {
        //TODO handle cancellationToken
        var url = siteMap.Url!.ToString();
        Log.Information("Loading sitemap from: {Url}", url);
        var xmlOther = XElement.Load(url);
        var urls = xmlOther.Descendants(xmlOther.GetDefaultNamespace() + "loc").Select(node => node.Value).ToList() ?? new List<string>();
        Log.Information("Loaded sitemap: {Urls} urls", urls.Count);

        return Task.FromResult(
                new FetchedSiteMap(urls));
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