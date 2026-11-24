using Microsoft.Extensions.Options;
using Serilog;
using System.Collections.Concurrent;
using System.Diagnostics;
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
    private readonly List<string> _collectedCss = [];
    private readonly List<string> _collectedJavascript = [];
    private readonly List<string> _collectedInternalLinks = [];
    private readonly List<string> _collectedExternalLinks = [];

    private readonly CrawlerSettings _crawlSettings;

    private readonly Stopwatch _stopwatch = new();

    private SeleniumPageHandler? _pageHandler;

    private readonly HtmlExtractor _htmlExtractor = new();

    private readonly HttpClient _httpClient;

    private readonly RequestProcessor _requestProcessor;
    private readonly SiteMapService _siteMapService;
    private int _maxPages = 0;

    private Uri? _baseUri;
    bool _limitedCompanyCrawling = true;

    public CrawlService(IHttpClientFactory httpClientFactory, IOptions<CrawlerSettings> options, IOptions<RequestOptions> requestOptions)
    {
        _httpClient = httpClientFactory.CreateClient("Crawler");
        var sitemapClient = httpClientFactory.CreateClient("Sitemap");
        _siteMapService = new SiteMapService(sitemapClient);
        _crawlSettings = options.Value;
        _requestProcessor = new(requestOptions);
    }

    private async Task LoadSiteMaps(string url, CancellationToken cancellationToken)
    {
        Log.Information("loading Sitemaps from robots.txt file");
        var sitemapFiles = await _siteMapService.FetchSiteMapsAsync(url, _robotsService.SiteMaps, cancellationToken);
        Log.Information("Found {Count} sitemap files for domain: {Url}", sitemapFiles.Count(), url);

        var sitemapUrls = 0;

        HashSet<Uri> siteMapLinks = [];

        foreach (var sitemap in sitemapFiles)
        {
            foreach (var siteMapUrl in sitemap.Links)
            {
                siteMapLinks.Add(siteMapUrl);
            }
        }
        List<Uri> mapLinks;
        if (_limitedCompanyCrawling)
        {
            mapLinks = CompanyPriorityFilter.FilterAndSortLinks(siteMapLinks);
        }
        else
        {
            mapLinks = [.. siteMapLinks];
        }
        foreach (var link in mapLinks)
        {
            if (AddRequest(link))
            {
                sitemapUrls++;
            }
        }

        Log.Information("Added {Count} sitemap urls of total {Total} found to request queue for domain: {Url}", sitemapUrls, mapLinks.Count, url);
    }

    public async Task<CrawlResult> CrawlAsync(string url, bool limitedCompanyCrawling, bool useSitemaps, SessionEntry session, CancellationToken cancellationToken)
    {
        _limitedCompanyCrawling = limitedCompanyCrawling;

        if (limitedCompanyCrawling)
        {
            Log.Information("Starting crawl for domain: {Url} using filtering", url);
            _maxPages = _crawlSettings.MaxPagesCompanyCheck;
        }
        else
        {
            Log.Information("Starting crawl for domain: {Url}", url);
            _maxPages = _crawlSettings.MaxPages;
        }
        try
        {
            _baseUri = new Uri(url);
            await _robotsService.LoadRobotTxt(url, _httpClient, cancellationToken);
        }
        catch (UriFormatException ex)
        {
            Log.Information(ex, "Domain url {Url} was not correct encountered exception", url);
            return new CrawlResult([], [], [], [], [], []);
        }
        catch (InvalidOperationException ex)
        {
            Log.Information(ex, "Failed to load robot.txt");
            return new CrawlResult([], [], [], [], [], []);
        }
        catch (HttpRequestException ex)
        {
            Log.Information(ex, "Failed to load robot.txt");
            return new CrawlResult([], [], [], [], [], []);
        }

        AddRequest(_baseUri);

        if (useSitemaps)
        {
            await LoadSiteMaps(_baseUri!.ToString(), cancellationToken);
        }

        _pageHandler = new SeleniumPageHandler(session.Driver, _requestProcessor.RequestOptions);

        _stopwatch.Start();
        var delay = _robotsService.CrawlDelay(_crawlSettings.UserAgent);

        Log.Information("Crawl delay set to: {Delay}ms", delay.TotalMilliseconds);

        try
        {
            var resultCrawl = await ProcessAsync(async (requestResult, crawlState) =>
            {
                if (!string.IsNullOrEmpty(requestResult.HtmlContent))
                {
                    var parsedContent = await _htmlExtractor.Extract(crawlState.Location, requestResult);
                    AddResult(crawlState.Location, parsedContent);
                }
                else
                {
                    Log.Warning("Request for {Url} returned no content", crawlState.Location);
                }
            }, delay, cancellationToken);

            _stopwatch.Stop();

            Log.Information("Crawl completed for domain: {Url} in {Time} seconds with {Count} pages fetched", url, _stopwatch.Elapsed.TotalSeconds, _fetchedPages.Count);

            long crawledHtmlTotalLen = 0;

            foreach (var extractedPage in resultCrawl)
            {
                crawledHtmlTotalLen += extractedPage.HtmlContent.Length;
                _collectedHtml.Add(extractedPage.HtmlContent);
                _collectedText.Add(extractedPage.Text);
                _collectedCss.Add(extractedPage.CssContent);
                _collectedJavascript.Add(extractedPage.JavascriptContent);
                _collectedInternalLinks.AddRange(extractedPage.InternalLinks);
                _collectedExternalLinks.AddRange(extractedPage.ExternalLinks);
            }

            var result = new CrawlResult
            (
                _collectedHtml,
                _collectedText,
                _collectedCss,
                _collectedJavascript,
                _collectedInternalLinks,
                _collectedExternalLinks
            );
            return result;
        }
        catch (SeleniumAbortedException ex)
        {
            Log.Warning(ex, "The selenium driver was aborted while crawling {Url}", url);
            throw;
        }
    }

    //This is for the internal links
    private void AddLink(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            Log.Information("Skipping invalid discovered url: {Url}", url);
            return;
        }
        uri = StripFragmentUri(uri);
        var visitedUrl = GetVisitKey(uri);
        if (_visited.ContainsKey(visitedUrl))
        {
            return;
        }

        AddRequest(uri);
    }

    private static string GetVisitKey(Uri url)
    {
        var authority = url.IsDefaultPort
        ? url.Host
        : $"{url.Host}:{url.Port}";
        return $"{authority}{url.AbsolutePath}{url.Query}";
    }

    private static Uri StripFragmentUri(Uri url)
    {
        return new UriBuilder(url)
        {
            Fragment = null
        }.Uri;
    }

    public bool AddRequest(Uri url, bool isRetrying = false)
    {
        url = StripFragmentUri(url);

        if (!CrawlUrlPolicy.IsHttp(url))
        {
            Log.Information("Request for {Url} was not to http or https skipping request.", url);
            return false;
        }

        if (!CrawlUrlPolicy.IsSameHost(_baseUri!, url))
        {
            Log.Information("Request for host {UrlHost} is not the same as base host {BaseUrlHost} skipping request to url {Url}", url.Host, _baseUri!.Host, url);
            return false;
        }

        if (_maxPages > 0)
        {
            if (_fetchedPages.Count + _requestProcessor.PendingRequests >= _maxPages)
            {
                Log.Information("Crawl limit reached {Url} will be ignored", url);
                return false;
            }
        }

        bool notYetVisited = _visited.TryAdd(GetVisitKey(url), 0);

        if (!isRetrying && !notYetVisited)
        {
            Log.Information("Request for {Url} was already handled. Will not add again.", url);
            return false;
        }
        if (_robotsService.IsAllowed(url.LocalPath, _crawlSettings.UserAgent))
        {
            Log.Information("Added {Url} to request queue", url);
            _requestProcessor.AddRequest(url);
            return true;
        }
        else
        {
            Log.Information("Request for {Url} is disallowed by robots.txt file", url);
            return false;
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
                Log.Information("Successfully extracted request from {Url}.", url);
                AddResult(content);

                if (!robotHeader.NoFollow(_crawlSettings.UserAgent))
                {
                    IEnumerable<string> links;
                    if (!_limitedCompanyCrawling)
                    {
                        links = content.InternalLinks;
                    }
                    else
                    {
                        links = CompanyPriorityFilter.FilterAndSortLinks(content.InternalLinks);
                    }
                    foreach (var link in links)
                    {
                        AddLink(link);
                    }
                }
            }
        }
    }

    public async Task<IEnumerable<ExtractedPage>> ProcessAsync(Func<ProcessResult, CrawlUrlState, Task> responseHandler, TimeSpan delay, CancellationToken cancellation = default)
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
                    if (crawlState.Requests.Count < _crawlSettings.MaxRequestRetries)
                    {
                        Log.Information("Request for {Url} got an exception. Will retry later.", processResult.Location);
                        crawlState.Requests.Add(new Request
                        {
                            StartTime = processResult.StartTime,
                            ElapsedTime = processResult.ElapsedTime,
                        });
                        AddRequest(processResult.Location, isRetrying: true);
                    }
                    else
                    {
                        Log.Information("Request for {Url} got an exception. Retry limit {Limit} reached.", processResult.Location, _crawlSettings.MaxRequestRetries);
                    }
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
            _pageHandler!,
            delay,
            cancellation
            );
        Log.Information("Completed crawling {Count} pages", _fetchedPages.Count);
        return [.. _fetchedPages];
    }
}