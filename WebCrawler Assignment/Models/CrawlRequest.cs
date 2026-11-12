namespace WebCrawler.Models;

public sealed record CrawlRequest(
    string DomainName,
    bool CompanyCheck,
    bool? UseSitemaps
);