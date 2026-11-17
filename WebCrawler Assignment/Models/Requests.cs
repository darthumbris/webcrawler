namespace WebCrawler.Models;

public sealed record CrawlRequest(
    string DomainName,
    string Uuid,
    bool CompanyCheck,
    bool? UseSitemaps
);

public sealed record CompanyRequest(string DomainName, string Uuid);

public sealed record SessionOpenRequest(BrowserType BrowserType);

public sealed record SessionCloseRequest(string Uuid);