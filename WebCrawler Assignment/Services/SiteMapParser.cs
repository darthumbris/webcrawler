using Serilog;
using System.Xml;
using System.Xml.Linq;

namespace WebCrawler.Services;

public class SiteMapParser
{
    private bool ParseSiteMapUrl(XElement node, out Uri? url)
    {
        Uri? location = null;

        foreach (var el in node.Elements())
        {
            var name = el.Name.LocalName;
            if (name.Equals("loc", StringComparison.InvariantCultureIgnoreCase))
            {
                if (Uri.TryCreate(el.Value, UriKind.Absolute, out var parsedUrl))
                {
                    location = parsedUrl;
                }
            }
        }

        if (location == null)
        {
            url = null;
            return false;
        }

        url = location;
        return true;
    }

    public async Task<FetchedSiteMap> ParseXmlSiteMapAsync(Uri siteMapUrl, StreamReader reader, CancellationToken cancellationToken)
    {
        XDocument document;
        var fetchedSiteMap = new FetchedSiteMap(new List<Uri>(), new List<Uri>(), siteMapUrl);

        try
        {
            document = await XDocument.LoadAsync(reader, LoadOptions.None, cancellationToken);
        }
        catch (XmlException)
        {
            Log.Warning("Failed to parse sitemap XML.");
            return fetchedSiteMap;
        }

        foreach (var node in document.Elements())
        {
            var name = node.Name.LocalName;
            if (name.Equals("urlset", StringComparison.InvariantCultureIgnoreCase))
            {
                var links = new List<Uri>();

                foreach (var url in node.Elements())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (ParseSiteMapUrl(url, out var parsedUrl))
                    {
                        links.Add(parsedUrl!);
                    }
                }

                fetchedSiteMap = fetchedSiteMap with { Links = links };
            }
            if (name.Equals("sitemapindex", StringComparison.InvariantCultureIgnoreCase))
            {
                var sitemapIndices = new List<Uri>();

                foreach (var url in node.Elements())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (ParseSiteMapUrl(url, out var parsedIndexUrl))
                    {
                        sitemapIndices.Add(parsedIndexUrl!);
                    }
                }

                fetchedSiteMap = fetchedSiteMap with { SiteMaps = sitemapIndices };
            }
        }

        return fetchedSiteMap;
    }

    public async Task<FetchedSiteMap> ParsePlainTxtSiteMapAsync(Uri siteMapUrl, StreamReader reader, CancellationToken cancellationToken)
    {
        var links = new List<Uri>();
        string line;

        while ((line = await reader.ReadLineAsync()) != null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Uri.TryCreate(line, UriKind.Absolute, out var parsedUrl))
            {
                links.Add(parsedUrl);
            }
        }

        return new FetchedSiteMap(links, new List<Uri>(), siteMapUrl);
    }
}
