namespace WebCrawler.Models;

public sealed record CrawlResult(
    List<string> Html,
    List<string> Text,
    List<string> InternalLinks,
    List<string> ExternalLinks
);