namespace WebCrawler.Services;

using OpenQA.Selenium;
using Serilog;
using System.Xml.Linq;

public sealed record FetchedPage(
    string Url,
    string Html,
    string? Title);

public sealed record FetchedSiteMap
(
    IReadOnlyCollection<Uri> Links
);

public sealed class SeleniumPageFetcher
{
    private readonly IWebDriver _driver;

    public SeleniumPageFetcher(IWebDriver driver)
    {
        _driver = driver;
    }

    public async Task<string> FetchPageAsync(
        string url,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _driver.Navigate().GoToUrl(url);

        return _driver.PageSource;
    }

    public Task<FetchedPage> FetchRobot(string url, CancellationToken cancellationToken)
    {
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
        //TODO handle sitemaps that don't use xml for some reason?

        //this also doesn't handle sitemap indexing for now
        var url = siteMap.Url!.ToString();

        //TODO need to remove this navigate.goturl makes it slower
        //but then also need to properly handle the too many requests that happens
        //because it get's too fast
        _driver.Navigate().GoToUrl(url);

        Log.Information("Loading sitemap from: {Url}", url);
        var xmlOther = XElement.Load(url);
        var urls = xmlOther.Descendants(xmlOther.GetDefaultNamespace() + "loc").Select(node => node.Value).ToList() ?? new List<string>();
        Log.Information("Loaded sitemap: {Urls} urls", urls.Count);

        var uris = new List<Uri>();
        foreach (var sitemapUrl in urls)
        {
            uris.Add(new Uri(sitemapUrl));
        }

        return Task.FromResult(
                new FetchedSiteMap(uris));
    }

    public void Quit()
    {
        _driver.Quit();
    }
}