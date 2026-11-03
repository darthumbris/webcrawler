namespace WebCrawler.Services;

using Serilog;
using System.Net;

public sealed class SiteMapService
{

    private readonly HttpClient _httpClient;
    private static SiteMapParser _siteMapParser = new SiteMapParser();

    public SiteMapService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

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

    private static bool checkSiteMapContentType(string contentType) => contentType.Equals("text/xml", StringComparison.InvariantCultureIgnoreCase) || contentType.Equals("application/xml", StringComparison.InvariantCultureIgnoreCase) || contentType.Equals("/plain", StringComparison.InvariantCultureIgnoreCase);

    private async Task<FetchedSiteMap> FetchSiteMapAsync(Uri siteMapUrl, CancellationToken cancellationToken)
    {
        try
        {
            var resp = await _httpClient.GetAsync(siteMapUrl, cancellationToken);

            if (resp.IsSuccessStatusCode)
            {
                var contentType = resp.Content.Headers.ContentType?.MediaType;

                //TODO maybe need to handle compressed sitemaps?
                if (checkSiteMapContentType(contentType))
                {
                    using var stream = await resp.Content.ReadAsStreamAsync();
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
            return new FetchedSiteMap(new List<Uri>(), new List<Uri>(), siteMapUrl);
        }

        catch (Exception ex)
        {
            Log.Warning("Failed to load sitemap from {Url} with exception: {Text}", siteMapUrl, ex.Message);
            return new FetchedSiteMap(new List<Uri>(), new List<Uri>(), siteMapUrl);
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

