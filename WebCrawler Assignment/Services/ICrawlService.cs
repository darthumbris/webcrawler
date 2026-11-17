using OpenQA.Selenium;
using WebCrawler.Models;

namespace WebCrawler.Services;

public interface ICrawlService
{
    Task<CrawlResult> CrawlAsync(
        string url,
        bool limitedCompanyCrawling,
        bool useSitemaps,
        IWebDriver driver,
        CancellationToken cancellationToken
        );
}
