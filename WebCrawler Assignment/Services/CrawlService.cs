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

    private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(100);

    private readonly Stopwatch _stopwatch = new Stopwatch();

    private readonly SeleniumPageFetcher _pageFetcher;

    public CrawlService()
    {
        var chromeOptions = new ChromeOptions();
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
        LoadRobot(url).Wait();
        EnqueUrl(url);
        //TODO also need to handle the robots.txt and robots header for the domain to determine which urls are allowed to be crawled

        //TODO handle the delay between requests based on the robots.txt crawl-delay directive

        while (_queue.Count > 0)
        {
            string currentUrl = _queue.Dequeue();

            await _semaphore.WaitAsync(cancellationToken);

            try
            {
                await ProcessPageAsync(currentUrl, cancellationToken);
            }
            finally
            {
                _semaphore.Release();
            }
        }

        //TODO also need to handle the interal/external links and text etc.

        _pageFetcher.Quit();
        var result = new CrawlResult
        (
            _collectedHtml,
            new List<string> { "Sample text content" },
            new List<string> { "https://example.com/internal-link" },
            new List<string> { "https://external.com/external-link" }
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
        //TODO maybe have a message if url is not allowed?
        if (_robotsService.IsAllowed(url) && !_visited.Contains(url) && !_queue.Contains(url))
        {
            _queue.Enqueue(url);
        }
    }

    private async Task LoadRobot(string url)
    {
        Log.Information("Loading robots.txt for domain: {Url}", url);
        var fetchedRobotsTxt = await _pageFetcher.FetchRobot(url + "/robots.txt");
        _robotsService.LoadTxt(fetchedRobotsTxt.Html.Split('\n'));
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

            //only add the html etc if robot tags in header allows it
            if (_robotsService.CheckHeader(fetchedPage.Html))
            {
                _collectedHtml.Add(fetchedPage.Html);
                //ExtractedPage extractedPage = HtmlExtractor.Extract(fetchedPage.Html, url);
                //TODO also need to use the html extractor to get the txt and internal/external links
                //TODO need to enque discovered urls from the page to the queue for further crawling
            }
        }
        catch (Exception ex)
        {
            Log.Warning("Error fetching page {Url}: {Message}", url, ex.Message);
        }
    }
}

//TODO should coordinate the following
//robots policy (header .txt)
//URL frontier
//PageFetcher
//HtmlExtractor
//then combine into the crawl result and return it

//Order of crawling should be:
//Domain name -> URL Frontier -> robots policy check -> PageFetcher ->
//HtmlExtractor -> extracted results -> combine into CrawlResult
//HtmlExtractor -> discovered urls -> URL Frontier -> repeat until max pages or timeout reached