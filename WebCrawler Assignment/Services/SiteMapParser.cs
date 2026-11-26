using Microsoft.Extensions.Options;
using Serilog;
using System.Xml;
using System.Xml.Linq;
using WebCrawler.Configuration;

namespace WebCrawler.Services;

public class SiteMapParser(IOptions<CrawlerSettings> options)
{
    private readonly CrawlerSettings _settings = options.Value;
    private static bool ParseSiteMapUrl(XElement node, out Uri? url)
    {
        Uri? location = null;

        foreach (var el in node.Elements())
        {
            var name = el.Name.LocalName;
            if (name.Equals("loc", StringComparison.OrdinalIgnoreCase))
            {
                var value = el.Value.TrimStart('\uFEFF').Trim();
                if (Uri.TryCreate(value, UriKind.Absolute, out var parsedUrl))
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
        var fetchedSiteMap = new FetchedSiteMap([], [], siteMapUrl);

        try
        {
            document = await XDocument.LoadAsync(reader, LoadOptions.None, cancellationToken);
        }
        catch (XmlException)
        {
            Log.Warning("Failed to parse sitemap XML from {Url}.", siteMapUrl);
            return fetchedSiteMap;
        }

        foreach (var node in document.Elements())
        {
            var name = node.Name.LocalName;
            if (name.Equals("urlset", StringComparison.OrdinalIgnoreCase))
            {
                var links = new HashSet<Uri>();

                foreach (var url in node.Elements())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (ParseSiteMapUrl(url, out var parsedUrl))
                    {
                        links.Add(parsedUrl!);
                    }
                    if (links.Count >= _settings.MaxSitemapLinks)
                    {
                        Log.Information("Sitemap link limit {Limit} reached", _settings.MaxSitemapLinks);
                        break;
                    }
                }

                fetchedSiteMap = fetchedSiteMap with { Links = links };
            }
            if (name.Equals("sitemapindex", StringComparison.OrdinalIgnoreCase))
            {
                var sitemapIndices = new HashSet<Uri>();

                foreach (var url in node.Elements())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (ParseSiteMapUrl(url, out var parsedIndexUrl))
                    {
                        sitemapIndices.Add(parsedIndexUrl!);
                    }
                    if (sitemapIndices.Count >= _settings.MaxSitemapIndices)
                    {
                        Log.Information("Sitemap index limit {Limit} reached", _settings.MaxSitemapIndices);
                        break;
                    }
                }

                fetchedSiteMap = fetchedSiteMap with { SiteMaps = sitemapIndices };
            }
        }

        return fetchedSiteMap;
    }

    public async Task<FetchedSiteMap> ParsePlainTxtSiteMapAsync(Uri siteMapUrl, StreamReader reader, CancellationToken cancellationToken)
    {
        var links = new HashSet<Uri>();
        string? line;

        while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
        {
            var value = line.TrimStart('\uFEFF').Trim();
            cancellationToken.ThrowIfCancellationRequested();
            if (Uri.TryCreate(value, UriKind.Absolute, out var parsedUrl))
            {
                links.Add(parsedUrl);
            }
            if (links.Count >= _settings.MaxSitemapLinks)
            {
                Log.Information("Sitemap link limit {Limit} reached", _settings.MaxSitemapLinks);
                break;
            }
        }

        return new FetchedSiteMap(links, [], siteMapUrl);
    }
}
