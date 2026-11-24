namespace WebCrawler.Models;

public sealed class SeleniumAbortedException(string message, Exception? inner = null) : Exception(message, inner)
{
}
