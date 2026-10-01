namespace WebCrawler.Services;

public sealed record ExtractedPage(
    string Text,
    IReadOnlyCollection<string> InternalLinks,
    IReadOnlyCollection<string> ExternalLinks);

public interface IHTMLExtractor
{
    Task<ExtractedPage> Extract(
        string pageUrl,
        string html);
}
