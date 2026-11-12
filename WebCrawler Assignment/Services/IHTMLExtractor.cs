using WebCrawler.Models;

namespace WebCrawler.Services;

public sealed record ExtractedPage(
    string HtmlContent, //html 
    string Text,
    string CssContent,
    string JavascriptContent,
    IReadOnlyCollection<string> InternalLinks,
    IReadOnlyCollection<string> ExternalLinks);

public interface IHTMLExtractor
{
    Task<ExtractedPage> Extract(Uri baseUri, ProcessResult requestResult);
}
