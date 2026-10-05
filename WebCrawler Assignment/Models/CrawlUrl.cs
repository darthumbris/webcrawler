namespace WebCrawler.Models;

public sealed record CrawlUrl(
    Uri Location,
    string Title,
    string Text,
    string Rule
);