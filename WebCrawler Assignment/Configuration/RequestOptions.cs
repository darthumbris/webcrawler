namespace WebCrawler.Configuration
{
    public class RequestOptions
    {
        public int MaxSimultanousRequests { get; set; } = 5;
        
        //500ms delay between requests
        public TimeSpan RequestDelay { get; set; } = new TimeSpan(0, 0, 0, 0, 500);
        
        
        //750ms offset
        public TimeSpan RequestDelayOffset {  get; set; } = new TimeSpan(0, 0, 0, 0, 750);
        
        //20s timeout
        public TimeSpan RequestTimeOut { get; set; } = new TimeSpan(0, 0, 20);
    }
}
