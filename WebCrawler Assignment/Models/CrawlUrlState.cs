namespace WebCrawler.Models
{
    public class Request
    {
        public TimeSpan ElapsedTime { get; set; }
        public DateTime StartTime { get; set; }

        //TODO should maybe also have a statuscode or success
        //this way can try to retry requests if they failed?
    }

    public class CrawlUrlState
    {
        public required Uri Location { get; set; }
        //TODO after adding the success/statuscode to request can use this in the 
        //AddRequest method to check if the request needs to be added again or not
        public IList<Request> Requests { get; set; } = [];
    }
}
