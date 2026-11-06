using Serilog;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using WebCrawler.Configuration;
using WebCrawler.Models;

namespace WebCrawler.Services;

public sealed class CrawlService : ICrawlService
{
    private readonly RobotsService _robotsService = new();
    private readonly ConcurrentDictionary<string, byte> _visited = new();
    private readonly ConcurrentBag<ExtractedPage> _fetchedPages = [];
    private readonly ConcurrentDictionary<Uri, CrawlUrlState> _crawlStates = new();

    private readonly List<string> _collectedHtml = [];
    private readonly List<string> _collectedText = [];
    private readonly List<string> _collectedInternalLinks = [];
    private readonly List<string> _collectedExternalLinks = [];

    private readonly CrawlerSettings _crawlSettings = new();

    private readonly Stopwatch _stopwatch = new();

    private readonly SeleniumPageHandler _pageHandler;

    private readonly HtmlExtractor _htmlExtractor = new();

    private readonly HttpClient _httpClient = new();

    private readonly RequestProcessor _requestProcessor = new();
    private readonly SiteMapService _siteMapService;

    private Uri? _baseUri;

    public CrawlService()
    {
        //For Local testing don't use the hubUrl
        string hubUrl = "http://localhost:4444/wd/hub";
        _pageHandler = new SeleniumPageHandler(BrowserType.Firefox, hubUrl);
        Log.Information("Done creating webdrivers");
        //_pageHandler = new SeleniumPageHandler(BrowserType.Firefox);
        _siteMapService = new SiteMapService(_httpClient);
    }

    public async Task<CrawlResult> CrawlAsync(string url, CancellationToken cancellationToken)
    {
        if (!(url.StartsWith("http://") || url.StartsWith("https://")))
        {
            url = "https://" + url;
        }

        _baseUri = new Uri(url);
        Log.Information("Starting crawl for domain: {Url}", url);
        await LoadRobot(url, cancellationToken);

        AddRequest(_baseUri);
        var sitemapFiles = await _siteMapService.FetchSiteMapsAsync(url, _robotsService.SiteMaps, cancellationToken);
        Log.Information("Found {Count} sitemap files for domain: {Url}", sitemapFiles.Count(), url);

        var sitemapUrls = 0;

        //TODO might be better to first sort the siteMapLinks
        //using a priority check
        //and then add the sorted links
        foreach (var sitemap in sitemapFiles)
        {
            foreach (var siteMapUrl in sitemap.Links)
            {
                sitemapUrls++;
                AddRequest(siteMapUrl);
            }
        }
        Log.Information("Added {Count} sitemap urls to request queue for domain: {Url}", sitemapUrls, url);

        _stopwatch.Start();
        var delay = _robotsService.CrawlDelay(_crawlSettings.UserAgent);

        Log.Information("Crawl delay set to: {Delay} seconds", delay.TotalSeconds);

        var resultCrawl = await ProcessAsync(async (requestResult, crawlState) =>
        {
            if (!string.IsNullOrEmpty(requestResult.HtmlContent))
            {
                var parsedContent = await _htmlExtractor.Extract(crawlState.Location, requestResult.HtmlContent, requestResult.TextContent ?? string.Empty);
                AddResult(crawlState.Location, parsedContent);
            }
            else
            {
                Log.Warning("Request for {Url} returned no content", crawlState.Location);
            }
        }, cancellationToken);

        _stopwatch.Stop();

        Log.Information("Crawl completed for domain: {Url} in {Time} seconds with {Count} pages fetched, visited urls {Urls} {CountVisited}", url, _stopwatch.Elapsed.TotalSeconds, _fetchedPages.Count, string.Join(", ", _visited.Keys), _visited.Count);

        _pageHandler.Quit();

        foreach (var extractedPage in resultCrawl)
        {
            _collectedHtml.Add(extractedPage.HtmlContent);
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
        var uri = StripFragmentUri(new Uri(url));
        var visitedUrl = StripPrefix(uri);
        if (_visited.ContainsKey(visitedUrl))
        {
            return;
        }

        AddRequest(uri);
    }

    private static string StripPrefix(Uri url)
    {
        var stripped = url.ToString();
        if (url.Scheme == Uri.UriSchemeHttp)
        {
            return stripped.TrimStart("http://").ToString();
        }
        else
        {
            return stripped.TrimStart("https://").ToString();
        }
    }

    private static Uri StripFragmentUri(Uri url)
    {
        return new UriBuilder(url)
        {
            Fragment = null
        }.Uri;
    }

    //This makes sure that only baseHost with subdomain www or www2 is allowed
    private bool IsSameAsBaseHost(Uri url)
    {
        return url.Host == _baseUri!.Host || url.Host.TrimStart("www.").ToString() == _baseUri!.Host.TrimStart("www.").ToString() || _baseUri!.Host.TrimStart("www2.").ToString() == url.Host.TrimStart("www2.").ToString();
    }

    public void AddRequest(Uri url)
    {
        url = StripFragmentUri(url);

        if (!IsSameAsBaseHost(url))
        {
            Log.Information("Request for host {UrlHost} is not the same as base host {BaseUrlHost} skipping request to url {Url}", url.Host, _baseUri!.Host, url);
            return;
        }

        if (_crawlSettings.MaxPages > 0)
        {
            if (_fetchedPages.Count + _requestProcessor.PendingRequests == _crawlSettings.MaxPages)
            {
                Log.Information("Crawl limit reached {Url} will be ignored", url);
                return;
            }
        }

        bool notYetVisited = _visited.TryAdd(StripPrefix(url), 0);

        if (notYetVisited && _robotsService.IsAllowed(url.ToString(), _crawlSettings.UserAgent))
        {
            Log.Information("Added {Url} to request queue", url);
            _requestProcessor.AddRequest(url);
        }
        else if (notYetVisited)
        {
            Log.Information("Request for {Url} is disallowed by robots.txt file", url);
        }
        else
        {
            Log.Warning("Request for {Url} was already handled. Will not add again.", url);
        }
    }

    public void AddResult(ExtractedPage page)
    {
        _fetchedPages.Add(page);
    }

    public void AddResult(Uri url, ExtractedPage content)
    {
        if (_crawlStates.TryGetValue(url, out var _))
        {
            var robotHeader = _robotsService.ParseHeader(content.HtmlContent, _crawlSettings.UserAgent);
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
                    //TODO might be better to first sort the internalLinks
                    //using a priority check
                    //and then add the sorted links
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
            _pageHandler,
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
        var robotsUrl = new Uri("https://" + new Uri(url).Host + "/robots.txt");

        using var response = await _httpClient.GetAsync(robotsUrl, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        if (response.StatusCode == HttpStatusCode.NotFound || response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
        {
            Log.Information("No robots.txt found for {Url}. Assuming all pages are allowed.", robotsUrl);
        }
        else if ((int)response.StatusCode >= 200 && (int)response.StatusCode < 300)
        {
            var fetchedRobotsTxt = await response.Content.ReadAsStringAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            _robotsService.LoadTxt(fetchedRobotsTxt);
        }
    }
}