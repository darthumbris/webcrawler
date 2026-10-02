using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using Serilog;
using System.Diagnostics;
using WebCrawler.Models;

namespace WebCrawler.Services;

public sealed class CrawlService : ICrawlService
{
    private readonly RobotsService _robotsService = new RobotsService();
    private readonly HashSet<string> _visited = new HashSet<string>();
    private readonly Queue<string> _queue = new Queue<string>();

    private readonly List<string> _collectedHtml = new List<string>();
    private readonly List<string> _collectedText = new List<string>();
    private readonly List<string> _collectedInternalLinks = new List<string>();
    private readonly List<string> _collectedExternalLinks = new List<string>();

    private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(100);

    private readonly Stopwatch _stopwatch = new Stopwatch();

    private readonly SeleniumPageFetcher _pageFetcher;

    private readonly HtmlExtractor _htmlExtractor = new HtmlExtractor();

    public CrawlService()
    {
        var chromeOptions = new ChromeOptions();
        //chromeOptions.AddArguments(["--disable-infobars", "--lang=en_US", "--window-position=0,0", "--window-size=5,5"]);
        //Some sites will disable the robots.txt loading when headless????
        chromeOptions.AddArguments("--headless=new"); // comment out for testing
        //TODO maybe also need to add the commandTimeOut?  
        IWebDriver driver = new ChromeDriver(chromeOptions);
        SeleniumPageFetcher pageFetcher = new SeleniumPageFetcher(driver);
        _pageFetcher = pageFetcher;
    }


    //TODO use selenium grid to run multiple instances of the crawler in parallel
    public async Task<CrawlResult> CrawlAsync(
string url,
CancellationToken cancellationToken)
    {
        Log.Information("Starting crawl for domain: {Url}", url);
        await LoadRobot(url, cancellationToken);
        await LoadSiteMaps(_robotsService.SiteMaps,cancellationToken);

        _stopwatch.Start();
        var delay = _robotsService.CrawlDelay();

        Log.Information("Crawl delay set to: {Delay} seconds", delay.TotalSeconds);
        EnqueUrl(url);

        //TODO maybe also handle the depth of the crawl
        //TODO also maybe use config for max pages to crawl
        while (_queue.Count > 0)
        {
            if (delay.TotalSeconds == 0 || _stopwatch.Elapsed > delay)
            {
                string currentUrl = _queue.Dequeue();

                await _semaphore.WaitAsync(cancellationToken);

                try
                {
                    await ProcessPageAsync(currentUrl, cancellationToken);
                }
                finally
                {
                    _stopwatch.Restart();
                    _semaphore.Release();
                }
            }

        }

        _pageFetcher.Quit();

        var result = new CrawlResult
        (
            _collectedHtml,
            _collectedText,
            _collectedInternalLinks,
            _collectedExternalLinks
        );
        return result;
    }

    public void EnqueUrl(string url)
    {
        //TODO maybe better check for valid url?
        if (!(url.StartsWith("http://") || url.StartsWith("https://")))
        {
            url = "https://" + url;
        }

        var robotAllowed = _robotsService.IsAllowed(url);
        if (robotAllowed && !_visited.Contains(url) && !_queue.Contains(url))
        {
            _queue.Enqueue(url);
        }
        else if (!robotAllowed)
        {
            Log.Information("URL is not allowed by robots.txt: {Url}", url);
        }
    }

    private async Task LoadRobot(string url, CancellationToken cancellationToken)
    {
        if (!(url.StartsWith("http://") || url.StartsWith("https://")))
        {
            url = "https://" + url;
        }
        var hostUrl = "https://" + new Uri(url).Host + "/robots.txt";
        var fetchedRobotsTxt = await _pageFetcher.FetchRobot(hostUrl, cancellationToken);
        _robotsService.LoadTxt(fetchedRobotsTxt.Html);
    }

    private async Task LoadSiteMaps(List<SiteMap> siteMaps, CancellationToken cancellationToken)
    {
        //This will load the sitemaps from robots and will enque these first (priority)

        if (siteMaps == null)
        {
            return;
        }
        foreach (var siteMap in siteMaps)
        {
            var fetchedSiteMap = await _pageFetcher.FetchSiteMap(siteMap, cancellationToken);
            List<SiteMap> includedSiteMaps = new();
            foreach (string url in fetchedSiteMap.Links)
            {
                //either load an included sitemap or enque the link in the sitemap
                if (url.EndsWith(".xml"))
                {
                    includedSiteMaps.Add(SiteMap.FromUrl(url));
                } else
                {
                    EnqueUrl(url);
                }
            }
            //All the sitemaps that are included in sitemaps need to also load their links
            if (includedSiteMaps.Count > 0)
            {
                await LoadSiteMaps(includedSiteMaps, cancellationToken);
            }
        }
    }

    private async Task ProcessPageAsync(string url, CancellationToken cancellationToken)
    {
        if (_visited.Contains(url))
        {
            return;
        }
        _visited.Add(url);
        Log.Information("Visiting page: {Url}", url);

        try
        {
            var fetchedPage = await _pageFetcher.FetchAsync(url, cancellationToken);

            if (_robotsService.CheckHeader(fetchedPage.Html))
            {
                _collectedHtml.Add(fetchedPage.Html);
                ExtractedPage extractedPage = await _htmlExtractor.Extract(url, fetchedPage.Html);
                _collectedText.Add(extractedPage.Text);
                foreach (var internalLink in extractedPage.InternalLinks)
                {
                    //EnqueUrl(internalLink.ToString()); //TODO enable this again
                    _collectedInternalLinks.Add(internalLink.ToString());
                }
                foreach (var externalLink in extractedPage.ExternalLinks)
                {
                    _collectedExternalLinks.Add(externalLink.ToString());
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning("Error fetching page {Url}: {Message}", url, ex.Message);
        }
    }
}

//TODO should handle the following
//URL frontier (priority pages)

//Order of crawling should be:
//Domain name -> URL Frontier -> robots policy check -> PageFetcher ->
//HtmlExtractor -> extracted results -> combine into CrawlResult
//HtmlExtractor -> discovered urls -> URL Frontier -> repeat until max pages or timeout reached