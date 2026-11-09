using HtmlAgilityPack;
using Serilog;
using WebCrawler.Configuration;
using WebCrawler.Models;

namespace WebCrawler.Services;

public class CompanyChecker : ICompanyChecker
{
    private readonly RobotsService _robotsService = new();
    private readonly HttpClient _httpClient = new();
    private readonly CrawlerSettings _crawlSettings = new();
    private readonly SchemaParser _schemaParser = new();
    private readonly HtmlExtractor _htmlExtractor = new();

    private Uri? _baseUri;

    private int _score;
    private List<CompanyCheckReason> _reasons = [];

    public CompanyChecker()
    {
        Log.Information("created new CompanyChecker class");
    }

    //public RobotsService GetRobotsService()
    //{

    //}

    public async Task<CompanyCheckResult> CheckCompanyAsync(string domainUrl, CancellationToken cancellationToken)
    {
        _score = 0;
        _reasons.Clear();

        if (!(domainUrl.StartsWith("http://") || domainUrl.StartsWith("https://")))
        {
            domainUrl = "https://" + domainUrl;
        }

        _baseUri = new Uri(domainUrl);

        string hubUrl = "http://localhost:4444/wd/hub";
        var pageHandler = new SeleniumPageHandler(BrowserType.Firefox, hubUrl, 1);

        await _robotsService.LoadRobotTxt(domainUrl, _httpClient, cancellationToken);

        if (!_robotsService.IsAllowed(domainUrl, _crawlSettings.UserAgent))
        {
            Log.Information("Robot disallows checking {Url} stopping company check.", domainUrl);
            pageHandler.Quit();
            return new CompanyCheckResult(false, -5, [CompanyCheckReason.RobotRefusal]);
        }

        Log.Information("Checking if {Url} is a company website", domainUrl);

        var html = await pageHandler.FetchPageAsync(domainUrl, 0, cancellationToken);
        pageHandler.Quit();

        if (string.IsNullOrEmpty(html))
        {
            Log.Information("Failed to parse {Url}", domainUrl);
            return new CompanyCheckResult(false, 0, [CompanyCheckReason.ParseFailure]);
        }

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        CheckStructuredJson(doc);
        CheckCompanyPages(doc);
        CheckFooter(doc);
        //TODO figure out if need more checks
        //TODO figure out if the scoring is enough now?


        var result = new CompanyCheckResult
        (
            _score > 5,
            _score,
            _reasons
        );

        return result;
    }

    //this checks if the url contains a footer with company name etc in it
    private void CheckFooter(HtmlDocument doc)
    {
        Log.Information("Checking footer for company indicators");
        var nodes = doc.DocumentNode.SelectNodes("//footer | " +
            "//*[contains(@id, 'footer')] | " +
            "//*[contains(@class, 'footer')]");


        if (nodes == null || nodes.Count == 0)
        {
            return;
        }

        Log.Information("Found {Count} footers", nodes.Count);

        var footerText = nodes.Select(footer => HtmlEntity.DeEntitize(footer.InnerText))
            .Select(text => System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim())
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToList();

        var footerScore = 0;

        foreach (var footer in footerText)
        {
            //legal entity check
            if (footer.Contains("B.V.", StringComparison.Ordinal) ||
                footer.Contains("N.V.", StringComparison.Ordinal) ||
                footer.Contains("Ltd.", StringComparison.Ordinal) ||
                footer.Contains("V.O.F.", StringComparison.Ordinal) ||
                footer.Contains("GmbH", StringComparison.Ordinal))
            {
                Log.Information("Found {Text} as a legal entity indicator", footer);
                footerScore += 3;
            }

            //Kvk number
            if (footer.Contains("KvK", StringComparison.Ordinal) || 
                footer.Contains("BTW", StringComparison.Ordinal) ||
                footer.Contains("VAT", StringComparison.Ordinal))
            {
                Log.Information("Found {Text} as a company indicator", footer);
                footerScore += 5;
            }

        }

        if (footerScore > 0)
        {
            Log.Information("Footer contained {Count} company indicators", footerScore);
            _reasons.Add(CompanyCheckReason.CompanyFooterCheck);
        }
        _score += Math.Min(footerScore, 5);
    }

    //This checks if the url contains JSON-LD containing schema.org organization etc stuff
    private void CheckStructuredJson(HtmlDocument doc)
    {
        Log.Information("Checking for schema.org structured json.");
        if (doc.DocumentNode == null)
        {
            return;
        }

        var nodes = doc.DocumentNode.SelectNodes("//script[@type='application/ld+json']");

        if (nodes == null || nodes.Count == 0)
        {
            return;
        }

        var scripts = nodes.Select(script => script.InnerText);

        var schemas = _schemaParser.Parse(scripts);

        foreach (var schema in schemas)
        {
            if (schema.SchemaType != null)
            {
                if (schema.SchemaType.Equals("Corporation", StringComparison.OrdinalIgnoreCase) ||
                    schema.SchemaType.Equals("LocalBusiness", StringComparison.OrdinalIgnoreCase) ||
                    schema.SchemaType.Equals("Store", StringComparison.OrdinalIgnoreCase) ||
                    schema.SchemaType.Equals("ProfessionalService", StringComparison.OrdinalIgnoreCase) ||
                    schema.SchemaType.Equals("Restaurant", StringComparison.OrdinalIgnoreCase))
                {
                    Log.Information("Found {Type} as high company indicator", schema.SchemaType);
                    _score += 10;
                    _reasons.Add(CompanyCheckReason.CompanyJsonCheck);
                    return;
                }
                else if (schema.SchemaType.Equals("Organization", StringComparison.OrdinalIgnoreCase))
                {
                    Log.Information("Found {Type} as minor indicator", schema.SchemaType);
                    _score += 1;
                    _reasons.Add(CompanyCheckReason.CompanyJsonCheck);
                    return;
                }
            }

            if (schema.StreetAddress != null)
            {
                Log.Information("Address found in schema");
                _score += 1;
            }
        }
    }


    //This checks if there are /about /company /contact /legal /privacy etc pages
    private void CheckCompanyPages(HtmlDocument doc)
    {
        Log.Information("Checking internal links for company like pages");
        var internalLinks = _htmlExtractor.ExtractInternalLinks(_baseUri!, doc);

        var pageScore = 0;

        foreach (var link in internalLinks)
        {
            //TODO think of more indicators
            if (link.Contains("about", StringComparison.OrdinalIgnoreCase) ||
                link.Contains("privacy", StringComparison.OrdinalIgnoreCase) ||
                link.Contains("contact", StringComparison.OrdinalIgnoreCase) ||
                link.Contains("services", StringComparison.OrdinalIgnoreCase) ||
                link.Contains("info", StringComparison.OrdinalIgnoreCase)
                )
            {
                pageScore += 1;
            }
        }

        Log.Information("Found {Count} indicator pages", pageScore);

        if (pageScore > 0)
        {
            _reasons.Add(CompanyCheckReason.CompanyPagesCheck);
        }

        //This so that it doesn't increase the score too much
        _score += Math.Min(pageScore, 3);
    }
}
