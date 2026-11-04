using HtmlAgilityPack;
using Serilog;
using System.Globalization;
using WebCrawler.Models;

namespace WebCrawler.Services;

abstract class Rule(List<string> userAgents, int order)
{
    public List<string> UserAgents { get; } = userAgents;
    public int Order { get; } = order;

    public bool IsGlobal()
    {
        return UserAgents.Contains("*");
    }
}

class CrawlDelayRule : Rule
{
    public TimeSpan Delay { get; }
    public CrawlDelayRule(List<string> userAgents, string line, int order) : base(userAgents, order)
    {
        ArgumentNullException.ThrowIfNull(line);
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

    public AccessRule(List<string> userAgents, RobotsLine line, int order) : base(userAgents, order)
    {
        ArgumentNullException.ThrowIfNull(line);

        Allowed = string.Equals(line.Field, "allow", StringComparison.OrdinalIgnoreCase);
        var rawPath = line.Value ?? string.Empty;
        if (rawPath.Length > 0 && !rawPath.StartsWith('/'))
        {
            rawPath = "/" + rawPath;
        }
        Path = rawPath;
    }

    public bool IsAnyPathDisallowed()
    {
        return !Allowed && !string.IsNullOrEmpty(Path);
    }
}

public class SiteMap(Uri? url)
{
    public Uri? Url { get; } = url;

    internal static SiteMap FromLine(RobotsLine line)
    {
        ArgumentNullException.ThrowIfNull(line);

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

class HeaderRule(List<string> userAgents) : Rule(userAgents, 0) {}

public class RobotHeader
{
    private readonly List<HeaderRule> _noFollow = [];
    private readonly List<HeaderRule> _noIndex = [];

    private RobotHeader()
    {
        _noFollow = [];
        _noIndex = [];

    }

    internal static RobotHeader All()
    {
        return new RobotHeader();
    }

    public bool NoIndex(string userAgent)
    {
        if (_noIndex.Count == 0)
        {
            return false;
        }
        foreach (var rule in _noIndex)
        {
            if (rule.IsGlobal() || rule.UserAgents.Contains(userAgent))
            {
                return true;
            }
        }
        return false;
    }

    public bool NoFollow(string userAgent)
    {
        if (_noFollow.Count == 0)
        {
            return false;
        }
        foreach (var rule in _noFollow)
        {
            if (rule.IsGlobal() || rule.UserAgents.Contains(userAgent))
            {
                return true;
            }
        }
        return false;
    }

    public void AddNoIndex(string userAgent)
    {
        _noIndex.Add(new HeaderRule([ userAgent ]));
    }

    public void AddNoFollow(string userAgent)
    {
        _noFollow.Add(new HeaderRule([userAgent]));
    }
}

public sealed class RobotsService
{
    private readonly List<AccessRule> _globalRules = [];
    private readonly List<AccessRule> _specificRules = [];
    private readonly List<CrawlDelayRule> _crawlDelayRules = [];

    public List<SiteMap> SiteMaps { get; private set; } = [];

    public bool IsDisallowed { get; private set; }
    public bool IsMalformed { get; private set; }
    public bool HasRobotRules { get; private set; }

    public RobotsService() {}

    public void LoadTxt(string content)
    {
        _globalRules.Clear();
        _specificRules.Clear();
        _crawlDelayRules.Clear();

        if (string.IsNullOrWhiteSpace(content))
        {
            return;
        }

        var lines = content.Split(["\r\n", "\r", "\n"], StringSplitOptions.RemoveEmptyEntries)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToArray();

        if (lines.Length == 0)
        {
            return;
        }

        List<string> userAgents = [];
        bool reset = false;

        int order = 0; //for what order useragent is

        foreach (var raw in lines)
        {
            var line = new RobotsLine(raw);

            switch (line.Type)
            {
                case LineType.Rule:
                case LineType.DelayRule:
                    reset = true;
                    if (userAgents.Count == 0)
                    {
                        IsMalformed = true;
                        break;
                    }
                    if (line.Type == LineType.Rule)
                    {
                        var rule = new AccessRule(userAgents, line, order++);
                        if (rule.IsGlobal())
                        {
                            _globalRules.Add(rule);
                        }
                        else
                        {
                            _specificRules.Add(rule);
                        }
                        if (rule.IsAnyPathDisallowed())
                        {
                            IsDisallowed = true;
                        }
                    }
                    else
                    {
                        Log.Information("Adding crawl delay rule for user agent {Text} with delay {Count}", userAgents.Last(), line.Value);
                        _crawlDelayRules.Add(new CrawlDelayRule(userAgents, line.Value!, order++));
                    }
                    HasRobotRules = true;
                    break;
                case LineType.Unknown:
                    Log.Information("Unknown line in robots.txt: {Text}", line.Raw);
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
                    if (reset)
                    {
                        userAgents.Clear();
                        reset = false;
                    }
                    userAgents.Add(line.Value!);
                    break;
            }
        }

        if (IsMalformed)
            Log.Warning("robot.txt is malformed");

        Log.Information("robots.txt loaded with {Count} rules and {Count} sitemaps", _globalRules.Count + _specificRules.Count, SiteMaps.Count);
    }

    public RobotHeader ParseHeader(string html, string userAgent)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        var metaNodes = doc.DocumentNode.SelectNodes("//meta");
        if (metaNodes == null)
        {
            return RobotHeader.All();
        }

        var robotHeader = RobotHeader.All();

        foreach (var metaNode in metaNodes)
        {
            string content = metaNode.GetAttributeValue("content", string.Empty);
            string name = metaNode.GetAttributeValue("name", string.Empty);
            if (string.Equals(name, "robots", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, userAgent, StringComparison.OrdinalIgnoreCase)
                )
            {
                var ruleAgent = name.Equals("robots", StringComparison.OrdinalIgnoreCase) ? "*" : name;
                if (string.Equals(content, "noindex", StringComparison.OrdinalIgnoreCase))
                {
                    Log.Information("Header found robot meta rule for noIndex for userAgent {Text}", ruleAgent);
                    robotHeader.AddNoIndex(ruleAgent);
                }
                else if (string.Equals(content, "nofollow", StringComparison.OrdinalIgnoreCase))
                {
                    Log.Information("Header found robot meta rule for noFollow for userAgent {Text}", ruleAgent);
                    robotHeader.AddNoFollow(ruleAgent);
                }
                else if (string.Equals(content, "none", StringComparison.OrdinalIgnoreCase))
                {
                    Log.Information("Header found robot meta rule for none for userAgent {Text}", ruleAgent);
                    robotHeader.AddNoFollow(ruleAgent);
                    robotHeader.AddNoIndex(ruleAgent);
                }
            }
        }



        return robotHeader;
    }

    public bool IsAllowed(
        string url,
        string userAgent)
    {
        if (!IsDisallowed || !HasRobotRules)
        {
            return true;
        }

        url = FixUrl(url);

        var specificMatches = _specificRules
            .Where(x => x.UserAgents.Any(ua => userAgent.Contains(ua, StringComparison.InvariantCultureIgnoreCase)))
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
        if (!url.StartsWith('/'))
        {
            url = "/" + url;
        }

        while (url.Contains("//", StringComparison.Ordinal))
        {
            url = url.Replace("//", "/", StringComparison.Ordinal);
        }
        return url;
    }

    public TimeSpan CrawlDelay(string userAgent)
    {
        //if no rules or no crawl delays no delay
        if (!HasRobotRules || _crawlDelayRules.Count == 0)
        {
            return TimeSpan.Zero;
        }

        var globalDelays = _crawlDelayRules.Where(x => x.IsGlobal())
            .ToList();

        var specificDelays = _crawlDelayRules
            .Where(x => x.UserAgents.Any(ua => userAgent.Contains(ua, StringComparison.InvariantCultureIgnoreCase)))
            .ToList();

        if (globalDelays.Count == 0 && specificDelays.Count == 0)
        {
            Log.Information("No crawl delay rules found for user agent {Text}", userAgent);
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
