using Microsoft.AspNetCore.Mvc.RazorPages;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using WebCrawler.Models;
using Serilog;

namespace WebCrawler.Services;

public sealed class CrawlService: ICrawlService
{
    private readonly RobotsService _robotsService;
    private readonly HashSet<string> _visited = new HashSet<string>();
    private readonly Queue<string> _queue = new Queue<string>();

    private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(100);

    public async Task<CrawlResult> CrawlAsync(
string url,
CancellationToken cancellationToken)
    {
        var chromeOptions = new ChromeOptions();
        chromeOptions.AddArguments("--headless=new"); // comment out for testing
        IWebDriver driver = new ChromeDriver(chromeOptions);
        SeleniumPageFetcher pageFetcher = new SeleniumPageFetcher(driver);

        EnqueUrl(url);

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

        driver.Quit();
        // Implement the crawling logic here
        // For example, you can use HttpClient to fetch the HTML content of the domain
        // and then parse it to extract internal and external links, as well as text content.
        // This is a placeholder implementation. You should replace it with your actual crawling logic.
        var result = new CrawlResult
        (
            new List<string> { },
            new List<string> { "Sample text content" },
            new List<string> { "https://example.com/internal-link" },
            new List<string> { "https://external.com/external-link" }
        );
        return Task.FromResult(result);
    }

    public void EnqueUrl(string url)
    {
        if (_robotsService.IsAllowed(url) && !_visited.Contains(url) && !_queue.Contains(url))
        {
            _queue.Enqueue(url);
        }
    }

    private async Task ProcessPageAsync(string url, CancellationToken cancellationToken)
    {
        if (_visited.Contains(url))
        {
            return;
        }
        _visited.Add(url);
        Log.Information("Visiting page: {}", url);

        try
        {
            var fetchedPage = pageFetcher.FetchAsync(domainName, cancellationToken).Result;
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