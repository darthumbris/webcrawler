namespace WebCrawler.Configuration;

public sealed class CrawlerSettings
{
    public int MaxPages { get; set; } = 250;

    //set the user agent to a custom value to identify the crawler
    public string UserAgent { get; set; } = "Mozilla/5.0";
}
