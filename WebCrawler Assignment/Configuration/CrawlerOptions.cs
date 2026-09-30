namespace WebCrawler.Configuration;

public sealed class CrawlerOptions
{
    public int MaxPages { get; set; } = 25;
    public int MaxDepth { get; set; } = 3;

    public int RequestTimeoutSeconds { get; set; } = 15;
    public int OverallTimeoutSeconds { get; set; } = 60;

    public int DelayBetweenRequestsMilliseconds { get; set; } = 500;

    public int MaxHtmlCharacters { get; set; } = 2_000_000;

    //set the user agent to a custom value to identify the crawler
    public string UserAgent { get; set; } = "Mozilla/5.0";
}
