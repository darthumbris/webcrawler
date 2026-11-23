namespace WebCrawler.Services;

using Serilog;

public sealed class SiteMapService(HttpClient httpClient)
{
    private readonly HttpClient _httpClient = httpClient;
    private static readonly SiteMapParser _siteMapParser = new();

    private static IEnumerable<Uri> LoadSiteMaps(Uri baseUrl, List<SiteMap> robotSiteMaps, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var sitemapsUrls = new HashSet<Uri>
        {
            new(baseUrl, "/sitemap.xml")
        };

        foreach (var sitemap in robotSiteMaps)
        {
            if (sitemap.Url is not null)
            {
                sitemapsUrls.Add(sitemap.Url);
            }
        }

        var allowedSitemaps = sitemapsUrls.Where(url => IsAllowedSitemap(url, baseUrl)).ToArray();
        return allowedSitemaps;
    }

    private static bool CheckSiteMapContentType(string contentType) => contentType.Equals("text/xml", StringComparison.OrdinalIgnoreCase) || contentType.Equals("application/xml", StringComparison.OrdinalIgnoreCase) || contentType.Equals("text/plain", StringComparison.OrdinalIgnoreCase);

    private async Task<FetchedSiteMap> FetchSiteMapAsync(Uri siteMapUrl, CancellationToken cancellationToken)
    {
        try
        {
            using var resp = await _httpClient.GetAsync(siteMapUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (!resp.IsSuccessStatusCode)
            {
                Log.Information("Sitemap response was not successful {Response} skipping {Url}.", resp.StatusCode, siteMapUrl);
                return new FetchedSiteMap([], [], siteMapUrl);
            }
            var contentType = resp.Content.Headers.ContentType?.MediaType ?? string.Empty;

            if (!CheckSiteMapContentType(contentType))
            {
                Log.Information("Sitemap of contenttype {Type} is not yet supported for {Url}", contentType, siteMapUrl);
                return new FetchedSiteMap([], [], siteMapUrl);
            }

            //TODO maybe need to handle compressed sitemaps?
            using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            using var reader = new StreamReader(stream);
            if (contentType.Equals("text/plain", StringComparison.OrdinalIgnoreCase))
            {
                return await _siteMapParser.ParsePlainTxtSiteMapAsync(siteMapUrl, reader, cancellationToken);
            }
            else
            {
                return await _siteMapParser.ParseXmlSiteMapAsync(siteMapUrl, reader, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            Log.Information("Request was cancelled stopping fetching sitemap");
            throw;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load sitemap from {Url}", siteMapUrl);
            return new FetchedSiteMap([], [], siteMapUrl);
        }
    }

    private static bool IsAllowedSitemap(Uri sitemapUrl, Uri baseUrl)
    {
        return (sitemapUrl.Scheme == Uri.UriSchemeHttp ||
            sitemapUrl.Scheme == Uri.UriSchemeHttps) &&
           sitemapUrl.Host.Equals(
               baseUrl.Host,
               StringComparison.OrdinalIgnoreCase);
    }

    public async Task<IEnumerable<FetchedSiteMap>> FetchSiteMapsAsync(string baseUrl, List<SiteMap> robotSiteMaps, CancellationToken cancellationToken)
    {
        Log.Information("Loading sitemap from: {Url}", baseUrl);

        var baseUri = new Uri(baseUrl);
        var fetchedSiteMaps = new Dictionary<Uri, FetchedSiteMap>();
        var discoveredSitemapUrls = new HashSet<Uri>(LoadSiteMaps(baseUri, robotSiteMaps, cancellationToken));
        var sitemapUrls = new Stack<Uri>(discoveredSitemapUrls);

        while (sitemapUrls.Count > 0)
        {
            var url = sitemapUrls.Pop();

            if (fetchedSiteMaps.ContainsKey(url))
            {
                continue;
            }

            if (!IsAllowedSitemap(url, baseUri))
            {
                Log.Warning("Skipping sitemap outside allowed host: {Url}", url);
                continue;
            }

            var sitemapFile = await FetchSiteMapAsync(url, cancellationToken);

            fetchedSiteMaps.Add(url, sitemapFile);

            foreach (var siteMapIndexUrl in sitemapFile.SiteMaps)
            {
                if (IsAllowedSitemap(siteMapIndexUrl, baseUri) &&
                    discoveredSitemapUrls.Add(siteMapIndexUrl))
                {
                    sitemapUrls.Push(siteMapIndexUrl);
                }
            }

        }

        return fetchedSiteMaps.Values;
    }
}

