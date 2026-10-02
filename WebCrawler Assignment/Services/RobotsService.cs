using HtmlAgilityPack;
using Serilog;
using System.Globalization;
using WebCrawler.Models;

namespace WebCrawler.Services;

abstract class Rule
{
    public string UserAgent { get; }
    public int Order { get; }
    public Rule(string userAgent, int order)
    {
        UserAgent = userAgent;
        Order = order;
    }

    public bool IsGlobal()
    {
        return UserAgent == "*";
    }
}

class CrawlDelayRule : Rule
{
    public TimeSpan Delay { get; }
    public CrawlDelayRule(string userAgent, string line, int order) : base(userAgent, order)
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

    public AccessRule(string userAgent, RobotsLine line, int order) : base(userAgent, order)
    {
        if (line is null)
        {
            throw new ArgumentNullException(nameof(line));
        }

        Allowed = string.Equals(line.Field, "allow", StringComparison.OrdinalIgnoreCase);
        var rawPath = line.Value ?? string.Empty;
        if (rawPath.Length > 0 && !rawPath.StartsWith("/"))
        {
            rawPath = "/" + rawPath;
        }
        Path = rawPath;
    }

    public bool isAnyPathDisallowed()
    {
        return !Allowed && !string.IsNullOrEmpty(Path);
    }
}

public class SiteMap
{
    public Uri? Url { get; }

    private SiteMap(Uri? url)
    {
        Url = url;
    }

    internal static SiteMap FromUrl(string url)
    {
        return new SiteMap(new Uri(url));
    }

    internal static SiteMap FromLine(RobotsLine line)
    {
        if (line is null)
        {
            throw new ArgumentNullException(nameof(line));
        }

        var url = line.Value ?? string.Empty;
        Uri? parsedUri = null;
        if (!string.IsNullOrWhiteSpace(url) &&
            Uri.TryCreate(url, UriKind.Absolute, out var candidate))
        {
            parsedUri = candidate;
        }

        return new SiteMap(parsedUri);
    }
}

public sealed class RobotsService
{
    private readonly List<AccessRule> _globalRules = new List<AccessRule>();
    private readonly List<AccessRule> _specificRules = new List<AccessRule>();
    private readonly List<CrawlDelayRule> _crawlDelayRules = new();

    public List<SiteMap> SiteMaps { get; private set; } = new();

    public bool IsDisallowed { get; private set; }
    public bool IsMalformed { get; private set; }
    public bool HasRobotRules { get; private set; }

    private readonly string _userAgent;

    public RobotsService()
    {
        //TODO get this working from the App.config file
        //_userAgent = ConfigurationManager.AppSettings.Get("UserAgent");
        _userAgent = "Mozilla/5.0";
    }

    public void LoadTxt(string content)
    {
        _globalRules.Clear();
        _specificRules.Clear();
        _crawlDelayRules.Clear();

        if (string.IsNullOrWhiteSpace(content))
        {
            return;
        }

        var lines = content.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToArray();

        if (lines.Length == 0)
        {
            return;
        }

        //TODO handle the case where there are multiple user agents before the rules
        string userAgent = string.Empty;

        int order = 0; //for what order useragent is

        foreach (var raw in lines)
        {
            var line = new RobotsLine(raw);

            switch (line.Type)
            {
                case LineType.Rule:
                case LineType.DelayRule:
                    if (string.IsNullOrEmpty(userAgent))
                    {
                        IsMalformed = true;
                        break;
                    }
                    if (line.Type == LineType.Rule)
                    {
                        var rule = new AccessRule(userAgent, line, order++);
                        if (rule.IsGlobal())
                        {
                            _globalRules.Add(rule);
                        }
                        else
                        {
                            _specificRules.Add(rule);
                        }
                        if (rule.isAnyPathDisallowed())
                        {
                            IsDisallowed = true;
                        }
                    }
                    else
                    {
                        Log.Information("Adding crawl delay rule for user agent {Text} with delay {Count}", userAgent, line.Value);
                        _crawlDelayRules.Add(new CrawlDelayRule(userAgent, line.Value!, order++));
                    }
                    HasRobotRules = true;
                    break;
                case LineType.Unknown:
                    IsMalformed = true;
                    break;
                case LineType.Sitemap:
                    var siteMap = SiteMap.FromLine(line);
                    if (siteMap.Url != null)
                    {
                        SiteMaps.Add(SiteMap.FromLine(line));
                    }
                    break;
                case LineType.Comment:
                    break;
                case LineType.UserAgent:
                    userAgent = line.Value!;
                    break;
            }
        }

        if (IsMalformed)
            Log.Warning("robot.txt is malformed");
    }

    public bool CheckHeader(string html)
    {
        //TODO not sure if this is what is meant with obey robot instructions in headers?
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        var metaNodes = doc.DocumentNode.SelectNodes("//meta");
        if (metaNodes == null)
        {
            return true;
        }

        foreach (var metaNode in metaNodes)
        {
            string content = metaNode.GetAttributeValue("content", string.Empty);
            string name = metaNode.GetAttributeValue("name", string.Empty);
            if (string.Equals(name, "robots", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, _userAgent, StringComparison.OrdinalIgnoreCase)
                )
            {
                if (string.Equals(content, "noindex", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(content, "none", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(content, "nofollow", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
        }

        return true;
    }


    public bool IsAllowed(
        string url)
    {
        if (!IsDisallowed || !HasRobotRules)
        {
            return true;
        }

        url = FixUrl(url);
        var specificMatches = _specificRules
            .Where(x => _userAgent.IndexOf(x.UserAgent, StringComparison.InvariantCultureIgnoreCase) >= 0)
            .ToList();

        //use speccific rules if they exist, otherwise use global rules
        var relevantRules = specificMatches.Count > 0
            ? specificMatches.Where(x =>
                string.IsNullOrEmpty(x.Path) ||
                IsUrlValid(url.Substring(1), x.Path.Substring(1)))
                .ToList()
            : _globalRules.Where(x =>
                string.IsNullOrEmpty(x.Path) ||
                IsUrlValid(url.Substring(1), x.Path.Substring(1)))
                .ToList();

        if (relevantRules.Count == 0)
            return true;

        //pick rule with longest path, then by order
        AccessRule rule = relevantRules
            .OrderByDescending(x => x.Path.Length)
            .ThenBy(x => x.Order)
            .First();

        return string.IsNullOrEmpty(rule.Path) || rule.Allowed;
    }

    public static bool IsUrlValid(string path, string rule)
    {
        var ruleLength = rule.Length;

        foreach (var c in rule.Select((value, i) => new { i, value }))
        {
            var ch = c.value;

            //exact match
            if (ch == '$' && c.i == ruleLength - 1)
            {
                return c.i == path.Length;
            }

            //wildcard
            if (ch == '*')
            {
                //wildcard at the end everything matches
                if (c.i == ruleLength - 1)
                {
                    return true;
                }

                //if wildcard is between
                for (int start = c.i; start < path.Length; start++)
                {
                    if (IsUrlValid(path[start..], rule[(c.i + 1)..]))
                    {
                        return true;
                    }
                }
                return false;
            }

            if (c.i >= path.Length || ch != path[c.i])
            {
                return false;
            }
        }

        //rule is a prefix so path must start with it
        return path.StartsWith(rule, StringComparison.OrdinalIgnoreCase);
    }

    private static string FixUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return "/";
        }
        if (!url.StartsWith("/", StringComparison.Ordinal))
        {
            url = "/" + url;
        }

        while (url.Contains("//", StringComparison.Ordinal))
        {
            url = url.Replace("//", "/", StringComparison.Ordinal);
        }
        return url;
    }

    public TimeSpan CrawlDelay()
    {
        //if no rules or no crawl delays no delay
        if (!HasRobotRules || _crawlDelayRules.Count == 0)
        {
            return TimeSpan.Zero;
        }

        var globalDelays = _crawlDelayRules.Where(x => x.IsGlobal())
            .ToList();
        var specificDelays = _crawlDelayRules.Where(x => x.UserAgent.IndexOf(_userAgent, StringComparison.InvariantCultureIgnoreCase) >= 0)
            .ToList();

        if (globalDelays.Count == 0 && specificDelays.Count == 0)
        {
            Log.Information("No crawl delay rules found for user agent {Text}", _userAgent);
            return TimeSpan.Zero;
        }

        if (specificDelays.Count > 0)
        {
            return specificDelays.First().Delay;
        }
        else
        {
            return globalDelays.First().Delay;
        }
    }
}
