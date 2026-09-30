using WebCrawler.Models;

namespace WebCrawler.Services;

public interface ICrawlService
{
    Task<CrawlResult> CrawlAsync(
string domainName,
CancellationToken cancellationToken);
}
