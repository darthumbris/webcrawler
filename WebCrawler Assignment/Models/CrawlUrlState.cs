namespace WebCrawler.Models
{
    public class Request
    {
        public TimeSpan ElapsedTime { get; set; }
        public DateTime StartTime { get; set;  }

        //should maybe also have a statuscode or success
        //(but selenium doesn't give response code for some reason...)
        //this way can try to retry requests if they failed?
    }

    public class CrawlUrlState
    {
        public Uri Location { get; set; }
        public IList<Request> Requests { get; set; } = new List<Request>();
    }
}
