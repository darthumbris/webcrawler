namespace WebCrawler.Services;

using Serilog;
using System.IO.Compression;

public sealed class SiteMapService(HttpClient httpClient)
{
    private readonly HttpClient _httpClient = httpClient;
    private static readonly SiteMapParser _siteMapParser = new();

    private static Uri[] LoadSiteMaps(Uri baseUrl, List<SiteMap> robotSiteMaps, CancellationToken cancellationToken)
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

        var allowedSitemaps = sitemapsUrls.Where(url => CrawlUrlPolicy.IsCrawlable(url, baseUrl)).ToArray();
        return allowedSitemaps;
    }

    private async Task<HttpResponseMessage?> GetSitemapResponseAsync(Uri sitemapUrl, Uri baseUri, CancellationToken cancellationToken)
    {
        const int maxRedirects = 5;

        var currentUrl = sitemapUrl;

        for (var redirectCount = 0; redirectCount <= maxRedirects; redirectCount++)
        {
            if (!CrawlUrlPolicy.IsCrawlable(currentUrl, baseUri))
            {
                Log.Warning("Skipping sitemap redirect outside allowed host: {Url}", currentUrl);
                return null;
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, currentUrl);
            var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if ((int)response.StatusCode is < 300 or >= 400)
            {
                return response;
            }

            var location = response.Headers.Location;
            response.Dispose();

            if (location is null)
            {
                Log.Warning("sitemap redirect had no location: {Url}", currentUrl);
                return null;
            }

            if (!Uri.TryCreate(currentUrl, location, out var nextUrl))
            {
                Log.Warning("Sitemap returned an invalid redirect location: {Location}", location);
                return null;
            }

            Log.Information("Following sitemap redirect from {SourceUrl} to {DestinationUrl}.",
                currentUrl, nextUrl);
            currentUrl = nextUrl;
        }

        Log.Warning("Sitemap exceeded the maximum amount of redirects {Url}", sitemapUrl);
        return null;
    }

    private static bool IsCompressed(string contentType)
    {
        return contentType.Equals("application/x-gzip", StringComparison.OrdinalIgnoreCase) ||
        contentType.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase);
    }
    private static bool CheckSiteMapContentType(string contentType) => contentType.Equals("text/xml", StringComparison.OrdinalIgnoreCase) || contentType.Equals("application/xml", StringComparison.OrdinalIgnoreCase) || contentType.Equals("text/plain", StringComparison.OrdinalIgnoreCase);

    private static string GetContentTypeFromExtension(string extension)
    {
        switch (extension.ToLower())
        {
            case ".txt":
            case ".conf":
            case ".def":
            case ".in":
            case ".ini":
            case ".list":
            case ".log":
            case ".text":
                return "text/plain";
            case ".xml":
            case ".rng":
            case "xsd":
            case "xsl":
                return "application/xml";
            default:
                return "application/octet-stream";
        }
    }

    private async Task<FetchedSiteMap?> FetchSiteMapAsync(Uri sitemapUrl, Uri baseUri, CancellationToken cancellationToken)
    {
        try
        {
            using var resp = await GetSitemapResponseAsync(sitemapUrl, baseUri, cancellationToken);

            if (resp is null)
            {
                return null;
            }

            if (!resp.IsSuccessStatusCode)
            {
                Log.Information("Sitemap response was not successful {Response} skipping {Url}.", resp.StatusCode, sitemapUrl);
                return null;
            }
            var requiresDecompression = false;
            var contentType = resp.Content.Headers.ContentType?.MediaType ?? string.Empty;

            if (IsCompressed(contentType))
            {
                requiresDecompression = true;
                var extension = Path.GetExtension(sitemapUrl.AbsolutePath) ?? string.Empty;
                contentType = GetContentTypeFromExtension(extension);
            }

            if (!CheckSiteMapContentType(contentType))
            {
                Log.Information("Sitemap of contenttype {Type} is not yet supported for {Url}", contentType, sitemapUrl);
                return null;
            }

            var finalUrl = resp.RequestMessage?.RequestUri ?? sitemapUrl;

            using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            var contentStream = stream;
            if (requiresDecompression)
            {
                contentStream = new GZipStream(contentStream, CompressionMode.Decompress);
            }

            using var reader = new StreamReader(contentStream);
            if (contentType.Equals("text/plain", StringComparison.OrdinalIgnoreCase))
            {
                return await _siteMapParser.ParsePlainTxtSiteMapAsync(finalUrl, reader, cancellationToken);
            }
            else
            {
                return await _siteMapParser.ParseXmlSiteMapAsync(finalUrl, reader, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            Log.Information("Request was cancelled stopping fetching sitemap");
            throw;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load sitemap from {Url}", sitemapUrl);
            return null;
        }
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

            if (!CrawlUrlPolicy.IsCrawlable(baseUri, url))
            {
                Log.Warning("Skipping sitemap outside allowed host: {Url}", url);
                continue;
            }

            var sitemapFile = await FetchSiteMapAsync(url, baseUri, cancellationToken);

            if (sitemapFile is null)
            {
                continue;
            }

            fetchedSiteMaps.Add(url, sitemapFile);

            foreach (var siteMapIndexUrl in sitemapFile.SiteMaps)
            {
                if (CrawlUrlPolicy.IsCrawlable(baseUri, siteMapIndexUrl) &&
                    discoveredSitemapUrls.Add(siteMapIndexUrl))
                {
                    sitemapUrls.Push(siteMapIndexUrl);
                }
            }

        }

        return fetchedSiteMaps.Values;
    }
}

