namespace WebCrawler.Services;

public sealed record ExtractedPage(
    string Text,
    IReadOnlyCollection<Uri> InternalLinks,
    IReadOnlyCollection<Uri> ExternalLinks);

public interface HTMLExtractor
{
    ExtractedPage Extract(
        Uri pageUrl,
        string html);
}
