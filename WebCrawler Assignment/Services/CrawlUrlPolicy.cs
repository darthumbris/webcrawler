namespace WebCrawler.Services;

//TODO maybe implement this so that CrawlService, SitemapService, RobotsService, HtmlExtractor and maybe other stuff uses this
//to check if it is ok
public sealed class CrawlUrlPolicy
{
    public static bool IsHttp(Uri uri)
    {
        return uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
    }

    public static bool IsSameHost(Uri baseUri, Uri uri)
    {
        return string.Equals(NormalizeHost(baseUri.Host), NormalizeHost(uri.Host), StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsCrawlable(Uri baseUri, Uri uri)
    {
        return IsSameHost(baseUri, uri) && IsHttp(uri);
    }

    private static string NormalizeHost(string host)
    {
        if (host.StartsWith("www2.", StringComparison.OrdinalIgnoreCase))
        {
            return host[5..];
        }

        if (host.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
        {
            return host[4..];
        }

        return host;
    }
}
