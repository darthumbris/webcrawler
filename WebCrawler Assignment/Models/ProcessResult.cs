using System.Diagnostics;

namespace WebCrawler.Models
{
    public class ProcessResult
    {
        public Uri Location { get; set; }
        public DateTime StartTime { get; set; }
        public double Delay { get; set; }
        //stream is better instead of the whole string
        public string Content { get; set; }
        public TimeSpan ElapsedTime { get; set; }
        public Exception Exception { get; set; }
    }

    public class ProcessInfo
    {
        public Uri Location { get; set; }
        //so that each requests has it's own timer
        public Stopwatch Timer { get; set; }
        //So can cancel each request
        public CancellationToken Cancellation { get; set; }
        public Double Delay { get; set; }
    }
}
