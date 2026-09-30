using System.Globalization;

namespace WebCrawler.Services;

abstract class Rule
{
    public string UserAgent { get; }
    public Rule(string userAgent)
    {
        UserAgent = userAgent;
    }
}

class CrawlDelayRule : Rule
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

    //check if there are any dissallowed pages
    public bool IsDisallowed { get; private set; }

    private readonly string _userAgent;

    public RobotsService()
    {
        //TODO get this working from the App.config file
        //_userAgent = ConfigurationManager.AppSettings.Get("UserAgent");
        _userAgent = "Mozilla/5.0";
    }

    public void LoadTxt(string[] lines)
    {
        //TODO parse the robots.txt rules here
    }

    public bool CheckHeader(string html)
    {
        //TODO parse the robots meta tag here
        return true;
    }


    public bool IsAllowed(
        string url)
    {
        if (!IsDisallowed)
        {
            return true;
        }

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

        //TODO now need to check which rule applies (by what order it was defined) and then check if the url is allowed or not
        return true;
    }

    public static bool IsUrlValid(string path, string rule)
    {
        //TODO need to check for wildcards too
        return path.StartsWith(rule, StringComparison.OrdinalIgnoreCase);
    }
}
