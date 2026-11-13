namespace WebCrawler.Models
{
    public class Request
    {
        public TimeSpan ElapsedTime { get; set; }
        public DateTime StartTime { get; set; }
    }

    public class CrawlUrlState
    {
        public required Uri Location { get; set; }
        public IList<Request> Requests { get; set; } = [];
    }
}
