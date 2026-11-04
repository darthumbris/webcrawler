using HtmlAgilityPack;

namespace WebCrawler.Services;

public class HtmlExtractor : IHTMLExtractor
{
    public async Task<ExtractedPage> Extract(
        Uri baseUri,
        string html,
        string textContent)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

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

        return new ExtractedPage(html, textContent, internalLinks, externalLinks);
    }
}
