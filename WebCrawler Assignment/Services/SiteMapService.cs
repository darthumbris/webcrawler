namespace WebCrawler.Services;

using Serilog;
using System.Net;

public sealed class SiteMapService(HttpClient httpClient)
{

    private readonly HttpClient _httpClient = httpClient;
    private static readonly SiteMapParser _siteMapParser = new();

    private async Task<IEnumerable<Uri>> LoadSiteMapsAsync(string baseUrl, List<SiteMap> robotSiteMaps, CancellationToken cancellationToken)
    {
        var defaultSitemapUrl = new Uri(new Uri(baseUrl), "/sitemap.xml");

        var siteMapsUrls = new List<Uri>
        {
            defaultSitemapUrl
        };

        siteMapsUrls.AddRange(robotSiteMaps.Select(s => s.Url!));
        siteMapsUrls = siteMapsUrls.Distinct().ToList();

        var loadedSiteMaps = new HashSet<Uri>();

        foreach (var url in siteMapsUrls)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var sitemapRequest = new HttpRequestMessage(HttpMethod.Head, url);
                var sitemapResponse = await _httpClient.SendAsync(sitemapRequest, cancellationToken);

                if (sitemapResponse.IsSuccessStatusCode)
                {
                    loadedSiteMaps.Add(url);
                    continue;
                }

                //check for too many requests etc
                if ((int)sitemapResponse.StatusCode >= 400 && (int)sitemapResponse.StatusCode < 500 && sitemapResponse.StatusCode != HttpStatusCode.NotFound)
                {
                    sitemapRequest = new HttpRequestMessage(HttpMethod.Get, url);
                    sitemapResponse = await _httpClient.SendAsync(sitemapRequest, cancellationToken);

                    if (sitemapResponse.IsSuccessStatusCode)
                    {
                        loadedSiteMaps.Add(url);
                    }
                }

            }
            catch (Exception ex)
            {
                Log.Warning("Failed to load sitemap from {Url} with exception: {Text}", url, ex.Message);
            }
        }
        return loadedSiteMaps;
    }

    private static bool CheckSiteMapContentType(string contentType) => contentType.Equals("text/xml", StringComparison.InvariantCultureIgnoreCase) || contentType.Equals("application/xml", StringComparison.InvariantCultureIgnoreCase) || contentType.Equals("/plain", StringComparison.InvariantCultureIgnoreCase);

    private async Task<FetchedSiteMap> FetchSiteMapAsync(Uri siteMapUrl, CancellationToken cancellationToken)
    {
        try
        {
            var resp = await _httpClient.GetAsync(siteMapUrl, cancellationToken);

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
                    if (contentType.Equals("/plain", StringComparison.InvariantCultureIgnoreCase))
                    {
                        return await _siteMapParser.ParsePlainTxtSiteMapAsync(siteMapUrl, reader, cancellationToken);
                    }
                    else
                    {
                        return await _siteMapParser.ParseXmlSiteMapAsync(siteMapUrl, reader, cancellationToken);
                    }
                }
            }
            return new FetchedSiteMap([], [], siteMapUrl);
        }

        catch (Exception ex)
        {
            Log.Warning("Failed to load sitemap from {Url} with exception: {Text}", siteMapUrl, ex.Message);
            return new FetchedSiteMap([], [], siteMapUrl);
        }
    }

    public async Task<IEnumerable<FetchedSiteMap>> FetchSiteMapsAsync(string baseUrl, List<SiteMap> robotSiteMaps, CancellationToken cancellationToken)
    {
        Log.Information("Loading sitemap from: {Url}", baseUrl);

        var fetchedSiteMaps = new Dictionary<Uri, FetchedSiteMap>();
        var sitemapUrls = new Stack<Uri>(await LoadSiteMapsAsync(baseUrl, robotSiteMaps, cancellationToken));

        while (sitemapUrls.Count > 0)
        {
            var url = sitemapUrls.Pop();
            var sitemapFile = await FetchSiteMapAsync(url, cancellationToken);

            if (sitemapFile != null)
            {
                fetchedSiteMaps.Add(url, sitemapFile);

                foreach (var siteMapIndexUrl in sitemapFile.SiteMaps)
                {
                    if (!fetchedSiteMaps.ContainsKey(siteMapIndexUrl))
                    {
                        sitemapUrls.Push(siteMapIndexUrl);
                    }
                }
            }
        }

        return fetchedSiteMaps.Values;
    }
}

