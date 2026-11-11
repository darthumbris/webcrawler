using Serilog;

namespace WebCrawler.Models;

//This should be used to reorder the internal links
//and filter out links for checking if a website is a company
public class CompanyPriorityFilter
{
    //TODO is there a need to filter out only the ones with score > 0?

    public static List<Uri> FilterAndSortLinks(IEnumerable<Uri> internalLinks)
    {
        Dictionary<Uri, int> links = [];

        foreach (var link in internalLinks)
        {
            var score = LinkScore(link.ToString());
            links.Add(link, score);
        }
        Log.Information("links before filter and sort: {Count}", links.Count);

        var sortedLinks = links.OrderBy(x => x.Value).ToList();
        var filteredLinks = sortedLinks.Where(f => f.Value > 0);
        var selectedLinks = filteredLinks.Select(a => a.Key).ToList();
        Log.Information("links after filter and sort: {Count}", selectedLinks.Count);
        return selectedLinks;
    }

    public static List<string> FilterAndSortLinks(IEnumerable<string> internalLinks)
    {
        Dictionary<string, int> links = [];

        foreach (var link in internalLinks)
        {
            var score = LinkScore(link);
            links.Add(link, score);
        }
        Log.Information("links before filter and sort: {Count}", links.Count);

        var sortedLinks = links.OrderBy(x => x.Value).ToList();
        var filteredLinks = sortedLinks.Where(f => f.Value > 0);
        var selectedLinks = filteredLinks.Select(a => a.Key).ToList();
        Log.Information("links after filter and sort: {Count}", selectedLinks.Count);
        return selectedLinks;
    }

    //TODO figure out how to score the pages
    private static int LinkScore(string url)
    {
        if (url.Contains("about", StringComparison.InvariantCultureIgnoreCase) ||
            url.Contains("over", StringComparison.InvariantCultureIgnoreCase))
        {
            return 1;
        }
        if (url.Contains("vacatures", StringComparison.InvariantCultureIgnoreCase) ||
            url.Contains("jobs", StringComparison.InvariantCultureIgnoreCase) ||
            url.Contains("contact", StringComparison.InvariantCultureIgnoreCase) ||
            url.Contains("career", StringComparison.InvariantCultureIgnoreCase) ||
            url.Contains("vacancies", StringComparison.InvariantCultureIgnoreCase))
        {
            return 2;
        }
        if (url.Contains("subscription", StringComparison.InvariantCultureIgnoreCase) ||
            url.Contains("customer", StringComparison.InvariantCultureIgnoreCase) ||
            url.Contains("klant", StringComparison.InvariantCultureIgnoreCase) ||
            url.Contains("order", StringComparison.InvariantCultureIgnoreCase) ||
            url.Contains("bestel", StringComparison.InvariantCultureIgnoreCase) ||
            url.Contains("product", StringComparison.InvariantCultureIgnoreCase) ||
            url.Contains("winkelwagen", StringComparison.InvariantCultureIgnoreCase) ||
            url.Contains("cart", StringComparison.InvariantCultureIgnoreCase))
        {
            return 3;
        }
        return 0;
    }
}
