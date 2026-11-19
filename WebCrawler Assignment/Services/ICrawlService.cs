using WebCrawler.Models;

namespace WebCrawler.Services;

public interface ICrawlService
{
    Task<CrawlResult> CrawlAsync(
        string url,
        bool limitedCompanyCrawling,
        bool useSitemaps,
        SessionEntry session,
        CancellationToken cancellationToken
        );
}
