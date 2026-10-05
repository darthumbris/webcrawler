namespace WebCrawler.Services;

public sealed record ExtractedPage(
    string RawContent, //html 
    string Text,
    IReadOnlyCollection<string> InternalLinks,
    IReadOnlyCollection<string> ExternalLinks);

public interface IHTMLExtractor
{
    Task<ExtractedPage> Extract(
        Uri baseUri,
        string html);
}
