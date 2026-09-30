using OpenQA.Selenium.DevTools;
using System.Globalization;
using WebCrawler.Configuration;
using System.Configuration;
using System.Collections.Specialized;

namespace WebCrawler.Services;

abstract class Rule
{
    public string UserAgent { get; }
    public Rule(string userAgent)
    {
        UserAgent = userAgent;
    }
}

class CrawlDelayRule: Rule
{
    public TimeSpan Delay { get; }
    public CrawlDelayRule(string userAgent, string line) : base(userAgent)
    {
        if (line is null)
        {
            throw new ArgumentNullException(nameof(line));
        }
        if (!double.TryParse(line,
                             NumberStyles.Float,
                             CultureInfo.InvariantCulture,
                             out var seconds))
        {
            seconds = 0.0;
        }

        Delay = TimeSpan.FromSeconds(seconds);
    }
}

class AccessRule : Rule
{
    //url path the rule applies to
    public string Path { get; }
    public bool Allowed { get; }

    public AccessRule(string userAgent, string line) : base(userAgent)
    {
        if (line is null)
        {
            throw new ArgumentNullException(nameof(line));
        }

        Allowed = line.StartsWith("Allow:", StringComparison.OrdinalIgnoreCase);
        Path = line.Substring(line.IndexOf(':') + 1).Trim();
    }
}

public sealed class RobotsService
{
    private readonly List<AccessRule> _globalRules = new List<AccessRule>();
    //for now can ignore the specific rules, I'll assume only using global rules
    private readonly List<AccessRule> _specificRules = new List<AccessRule>();
    private readonly List<CrawlDelayRule> _crawlDelayRules = new();

    public List<Sitemap> Sitemaps { get; private set; } = new();

    //check if there are any dissallowed pages
    public bool IsDisallowed { get; private set; }

    private readonly string _userAgent;

    RobotsService()
    {
        _userAgent = ConfigurationManager.AppSettings.Get("UserAgent");
    }


    public bool IsAllowed(
        string url)
    {
        if (!IsDisallowed)
        {
            return true;
        }

        string userAgent = CrawlerOptions.UserAgent;
        var specificMatches = _specificRules
            .Where(x => userAgent.IndexOf(x.UserAgent, StringComparison.InvariantCultureIgnoreCase) >= 0)
            .ToList();

        var globalMatches = _globalRules.Where(r => url.StartsWith(r.Path, StringComparison.OrdinalIgnoreCase)).ToList();
        //TODO check the access rules
        return true;
    }
}
