using HtmlAgilityPack;
using WebCrawler.Models;

namespace WebCrawler.Services;

public class HtmlExtractor : IHTMLExtractor
{
    public async Task<ExtractedPage> Extract(Uri baseUri, ProcessResult requestResult)
    {
        var textContent = requestResult.TextContent ?? string.Empty;
        var cssContent = requestResult.CssContent ?? string.Empty;
        var jsContent = requestResult.JavascriptContent ?? string.Empty;
        if (string.IsNullOrEmpty(requestResult.HtmlContent))
        {
            return new ExtractedPage(string.Empty, textContent, cssContent, jsContent, [], []);
        }
        var doc = new HtmlDocument();
        doc.LoadHtml(requestResult.HtmlContent);

        var links = doc.DocumentNode.SelectNodes("//a[@href]");
        var internalLinks = new HashSet<string>();
        var externalLinks = new HashSet<string>();

        if (links != null)
        {
            foreach (var link in links)
            {
                var href = link.GetAttributeValue("href", string.Empty);
                if (string.IsNullOrEmpty(href))
                {
                    continue;
                }

                if (Uri.TryCreate(baseUri, href, out Uri? resultUri))
                {
                    if (resultUri == null)
                        continue;
                    if (resultUri.Host == baseUri.Host)
                    {
                        internalLinks.Add(resultUri.ToString());
                    }
                    else
                    {
                        externalLinks.Add(resultUri.ToString());
                    }
                }
            }
        }

        return new ExtractedPage(requestResult.HtmlContent, textContent, cssContent, jsContent, internalLinks, externalLinks);
    }

    public IReadOnlyCollection<string> ExtractInternalLinks(Uri baseUri, HtmlDocument doc)
    {
        var links = doc.DocumentNode.SelectNodes("//a[@href]");
        var internalLinks = new HashSet<string>();

        if (links != null)
        {
            foreach (var link in links)
            {
                var href = link.GetAttributeValue("href", string.Empty);
                if (string.IsNullOrEmpty(href))
                {
                    continue;
                }

                if (Uri.TryCreate(baseUri, href, out Uri? resultUri))
                {
                    if (resultUri == null)
                        continue;
                    if (resultUri.Host == baseUri.Host)
                    {
                        internalLinks.Add(resultUri.ToString());
                    }
                }
            }
        }

        return internalLinks;
    }
}
