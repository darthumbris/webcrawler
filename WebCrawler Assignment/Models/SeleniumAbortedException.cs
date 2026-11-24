namespace WebCrawler.Models;

public sealed class SeleniumAbortedException : Exception
{
    public SeleniumAbortedException(string message, Exception? inner = null) : base(message, inner) { }
}
