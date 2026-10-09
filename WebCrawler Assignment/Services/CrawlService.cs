using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using Serilog;
using System.Collections.Concurrent;
using System.Diagnostics;
using WebCrawler.Configuration;
using WebCrawler.Models;

namespace WebCrawler.Services;

public sealed class CrawlService : ICrawlService
{
    private readonly RobotsService _robotsService = new RobotsService();
    private readonly ConcurrentDictionary<string, byte> _visited = new();
    private readonly ConcurrentBag<ExtractedPage> _fetchedPages = new();
    private ConcurrentDictionary<Uri, CrawlUrlState> _crawlStates = new();
    private readonly List<Uri> _sitemapUrls = new();

    private readonly List<string> _collectedHtml = new List<string>();
    private readonly List<string> _collectedText = new List<string>();
    private readonly List<string> _collectedInternalLinks = new List<string>();
    private readonly List<string> _collectedExternalLinks = new List<string>();

    private readonly CrawlerSettings _crawlSettings = new CrawlerSettings();

    private readonly Stopwatch _stopwatch = new Stopwatch();

    private readonly SeleniumPageFetcher _pageFetcher;

    private readonly HtmlExtractor _htmlExtractor = new HtmlExtractor();

    private RequestProcessor _requestProcessor = new();

    private Uri? _baseUri;

    public CrawlService()
    {
        //var chromeOptions = new ChromeOptions();
        //chromeOptions.AddArguments(["--disable-infobars", "--lang=en_US", "--window-position=0,0", "--window-size=5,5"]);
        //Some sites will disable the robots.txt loading when headless????
        //chromeOptions.AddArguments("--headless=new"); // comment out for testing
        string hubUrl = "http://localhost:4444/wd/hub";

        //For Local testing don't use the hubUrl
        IWebDriver driver = WebcrawlerDriver.CreateGridInstance(BrowserType.Firefox, hubUrl);
        //TODO use the WebDriver class here to create the driver
        SeleniumPageFetcher pageFetcher = new SeleniumPageFetcher(driver);
        _pageFetcher = pageFetcher;
    }

    public async Task<CrawlResult> CrawlAsync(
string url,
CancellationToken cancellationToken)
    {
        if (!(url.StartsWith("http://") || url.StartsWith("https://")))
        {
            url = "https://" + url;
        }

        _baseUri = new Uri(url);
        Log.Information("Starting crawl for domain: {Url}", url);
        await LoadRobot(url, cancellationToken);
        await LoadSiteMaps(_robotsService.SiteMaps, cancellationToken);
        foreach (var sitemapUrl in _sitemapUrls)
        {
            AddRequest(sitemapUrl);
        }

        _stopwatch.Start();
        var delay = _robotsService.CrawlDelay(_crawlSettings.UserAgent);

        Log.Information("Crawl delay set to: {Delay} seconds", delay.TotalSeconds);
        AddRequest(_baseUri);

        var resultCrawl = await ProcessAsync(async (requestResult, crawlState) =>
        {
            var parsedContent = await _htmlExtractor.Extract(crawlState.Location, requestResult.Content);
            AddResult(crawlState.Location, parsedContent);
        });

        _stopwatch.Stop();

        _pageFetcher.Quit();

        foreach (var extractedPage in resultCrawl)
        {
            _collectedHtml.Add(extractedPage.RawContent);
            _collectedText.Add(extractedPage.Text);
            _collectedInternalLinks.AddRange(extractedPage.InternalLinks);
            _collectedExternalLinks.AddRange(extractedPage.ExternalLinks);
        }

        var result = new CrawlResult
        (
            _collectedHtml,
            _collectedText,
            _collectedInternalLinks,
            _collectedExternalLinks
        );
        return result;
    }

    //This is for the interal links
    private void AddLink(string url)
    {
        var uri = new Uri(url);
        if (_visited.ContainsKey(uri.ToString()))
        {
            return;
        }

        AddRequest(uri);
    }

    public void AddRequest(Uri url)
    {
        if (url.Host != _baseUri!.Host)
        {
            Log.Information("Request for host {Url} is not the same as base host {Url}", url.Host, _baseUri.Host);
        }

        if (_crawlSettings.MaxPages > 0)
        {
            if (_fetchedPages.Count + _requestProcessor.PendingRequests == _crawlSettings.MaxPages)
            {
                //Log.Information("Crawl limit reached {Url} will be ignored", url);
                return;
            }
        }

        _visited.TryAdd(url.ToString(), 0);

        if (_robotsService.IsAllowed(url.ToString(), _crawlSettings.UserAgent))
        {
            Log.Information("Added {Url} to request queue", url);
            _requestProcessor.AddRequest(url);
        }
        else
        {
            Log.Information("Request for {Url} is disallowed by robots.txt file", url);
        }
    }

    public void AddResult(ExtractedPage page)
    {
        _fetchedPages.Add(page);
    }

    public void AddResult(Uri url, ExtractedPage content)
    {
        if (_crawlStates.TryGetValue(url, out var crawlState))
        {
            var robotHeader = _robotsService.ParseHeader(content.RawContent, _crawlSettings.UserAgent);
            if (robotHeader.NoIndex(_crawlSettings.UserAgent))
            {
                Log.Information("Page {Url} has been blocked by robot rules in header", url);
            }
            else
            {
                Log.Information("Sucesfully extracted request from {Url}.", url);
                AddResult(content);
                if (!robotHeader.NoFollow(_crawlSettings.UserAgent))
                {
                    foreach (var link in content.InternalLinks)
                    {
                        AddLink(link);
                    }
                }
            }
        }
    }

    public async Task<IEnumerable<ExtractedPage>> ProcessAsync(Func<ProcessResult, CrawlUrlState, Task> responseHandler, CancellationToken cancellation = default)
    {
        await _requestProcessor.ProcessAsync(
            async (processResult) =>
            {
                //check if request was already there otherwise create a new state
                var crawlState = _crawlStates.GetOrAdd(processResult.Location, new CrawlUrlState
                {
                    Location = processResult.Location
                });

                //retry the request if failed
                if (processResult.Exception != null)
                {
                    Log.Information("Request for {Url} got an exception. Will retry later.", processResult.Location);
                    crawlState.Requests.Add(new Request
                    {
                        StartTime = processResult.StartTime,
                        ElapsedTime = processResult.ElapsedTime,
                    });
                    AddRequest(processResult.Location);
                }
                else
                {

                    var request = new Request
                    {
                        StartTime = processResult.StartTime,
                        ElapsedTime = processResult.ElapsedTime,
                    };
                    crawlState.Requests.Add(request);

                    await responseHandler(processResult, crawlState);
                }
            },
            _pageFetcher,
            cancellation
            );
        Log.Information("Completed crawling {Count} pages", _fetchedPages.Count);
        return _fetchedPages.ToArray();
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
            foreach (Uri url in fetchedSiteMap.Links)
            {
                //either load an included sitemap or enque the link in the sitemap
                if (url.ToString().EndsWith(".xml"))
                {
                    includedSiteMaps.Add(new SiteMap(url));
                }
                else
                {
                    _sitemapUrls.Add(url);
                }
            }
            //All the sitemaps that are included in sitemaps need to also load their links
            if (includedSiteMaps.Count > 0)
            {
                await LoadSiteMaps(includedSiteMaps, cancellationToken);
            }
        }
    }
}