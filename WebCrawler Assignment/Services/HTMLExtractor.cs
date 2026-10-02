using HtmlAgilityPack;
using System.Text;
using System.Web;

namespace WebCrawler.Services;

public class HtmlExtractor : IHTMLExtractor
{
    public async Task<ExtractedPage> Extract(
        string pageUrl,
        string html)
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

                Uri baseUri = new Uri(pageUrl);

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

        var text = ExtractTextFromHtml(doc.DocumentNode.ChildNodes);

        return new ExtractedPage(text, internalLinks, externalLinks);
    }

    private static string ExtractTextFromHtml(HtmlNodeCollection nodes)
    {
        var text = new StringBuilder();

        //TODO improve this now it doesn't give nice text back
        //maybe also handle list/bullets/table etc?

        foreach (var node in nodes)
        {
            if (string.Equals(node.Name, "style", StringComparison.InvariantCultureIgnoreCase))
            {
                continue;
            }
            if (node.Name == "br")
            {
                text.AppendLine();
            }
            if (node.HasChildNodes)
            {
                text.Append(ExtractTextFromHtml(node.ChildNodes));
            }
            else
            {
                var innerText = node.InnerText;
                if (!string.IsNullOrWhiteSpace(innerText))
                {
                    var currentText = text.ToString();
                    bool collapsWhiteSpace = string.IsNullOrEmpty(currentText) || Char.IsWhiteSpace(currentText[currentText.Length - 1]);

                    innerText = HttpUtility.HtmlDecode(innerText);

                    foreach (char c in innerText)
                    {
                        if (Char.IsWhiteSpace(c))
                        {
                            if (!collapsWhiteSpace)
                            {
                                text.Append(' ');
                                collapsWhiteSpace = true;
                            }
                        }
                        else
                        {
                            text.Append(c);
                            collapsWhiteSpace = false;
                        }
                    }
                }
            }
        }

        if (text.Length > 0)
        {
            return text.ToString();
        }
        return string.Empty;
    }
}
