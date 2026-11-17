namespace WebCrawler.Configuration;

public sealed class CrawlerSettings
{
    public int MaxPages { get; set; } = 250;
    public int MaxPagesCompanyCheck { get; set; } = 50;
    public int MaxRequestRetries { get; set; } = 10;

    public int MaxChromeSessions { get; set; } = 5;
    public int MaxFirefoxSessions { get; set; } = 15;
    public int MaxEdgeSessions { get; set; } = 5;

    public string HubUrl { get; set; } = "http://localhost:4444/wd/hub";

    //set the user agent to a custom value to identify the crawler
    public string UserAgent { get; set; } = "Mozilla/5.0";
}
