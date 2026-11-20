namespace WebCrawler.Services;

using Serilog;
using System.Net;

public sealed class SiteMapService(HttpClient httpClient)
{
    private readonly HttpClient _httpClient = httpClient;
    private static readonly SiteMapParser _siteMapParser = new();

    private async Task<IEnumerable<Uri>> LoadSiteMapsAsync(string baseUrl, List<SiteMap> robotSiteMaps, CancellationToken cancellationToken)
    {
        var baseUri = new Uri(baseUrl);
        var defaultSitemapUrl = new Uri(baseUri, "/sitemap.xml");

        var siteMapsUrls = new List<Uri>
        {
            defaultSitemapUrl
        };

        siteMapsUrls.AddRange(robotSiteMaps.Select(s => s.Url).OfType<Uri>());
        siteMapsUrls = [.. siteMapsUrls.Distinct()];

        var loadedSiteMaps = new HashSet<Uri>();

        foreach (var url in siteMapsUrls)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!IsAllowedSitemap(url, baseUri))
            {
                Log.Warning("Skipping sitemap outside allowed host: {Url}", url);
                continue;
            }

            try
            {
                using var sitemapRequest = new HttpRequestMessage(HttpMethod.Head, url);
                using var sitemapResponse = await _httpClient.SendAsync(sitemapRequest, cancellationToken);

                if (sitemapResponse.IsSuccessStatusCode)
                {
                    loadedSiteMaps.Add(url);
                    continue;
                }

                //check for too many requests etc
                if ((int)sitemapResponse.StatusCode >= 400 && (int)sitemapResponse.StatusCode < 500 && sitemapResponse.StatusCode != HttpStatusCode.NotFound)
                {
                    using var sitemapRequestRetry = new HttpRequestMessage(HttpMethod.Get, url);
                    using var sitemapResponseRetry = await _httpClient.SendAsync(sitemapRequestRetry, cancellationToken);

                    if (sitemapResponseRetry.IsSuccessStatusCode)
                    {
                        loadedSiteMaps.Add(url);
                    }
                }

            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to load sitemap from {Url}", url);
            }
        }
        return loadedSiteMaps;
    }

    private static bool CheckSiteMapContentType(string contentType) => contentType.Equals("text/xml", StringComparison.OrdinalIgnoreCase) || contentType.Equals("application/xml", StringComparison.OrdinalIgnoreCase) || contentType.Equals("text/plain", StringComparison.OrdinalIgnoreCase);

    private async Task<FetchedSiteMap> FetchSiteMapAsync(Uri siteMapUrl, CancellationToken cancellationToken)
    {
        try
        {
            using var resp = await _httpClient.GetAsync(siteMapUrl, cancellationToken);

            if (resp.IsSuccessStatusCode)
            {
                var contentType = resp.Content.Headers.ContentType?.MediaType ?? string.Empty;

                //TODO maybe need to handle compressed sitemaps?
                if (CheckSiteMapContentType(contentType))
                {
                    using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();

                    var siteMapStream = stream;

                    using var reader = new StreamReader(siteMapStream);
                    if (contentType.Equals("text/plain", StringComparison.OrdinalIgnoreCase))
                    {
                        return await _siteMapParser.ParsePlainTxtSiteMapAsync(siteMapUrl, reader, cancellationToken);
                    }
                    else
                    {
                        return await _siteMapParser.ParseXmlSiteMapAsync(siteMapUrl, reader, cancellationToken);
                    }
                }
            }
            Log.Information("Sitemap response was not successful {Response} skipping {Url}.", resp.StatusCode, siteMapUrl);
            return new FetchedSiteMap([], [], siteMapUrl);
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

    //TODO maybe should allow sitemap outside domain?
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
        var sitemapUrls = new Stack<Uri>(await LoadSiteMapsAsync(baseUrl, robotSiteMaps, cancellationToken));
        //TODO maybe add the stupid HashSet discoveredSitemapUrls

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
                    !fetchedSiteMaps.ContainsKey(siteMapIndexUrl))
                {
                    sitemapUrls.Push(siteMapIndexUrl);
                }
            }

        }

        return fetchedSiteMaps.Values;
    }
}

