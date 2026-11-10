using HtmlAgilityPack;
using OpenQA.Selenium.BiDi.BrowsingContext;
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
    private readonly List<string> _reasons = [];

    public CompanyChecker()
    {
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:159.0) Gecko/20100101 Firefox/159.0");
    }

    public async Task<CompanyCheckResult> CheckCompanyAsync(string domainUrl, CancellationToken cancellationToken)
    {
        _score = 0;
        _reasons.Clear();
        _robotsService.Reset();

        Log.Information("domainUrl: {Url}", domainUrl);

        if (!(domainUrl.StartsWith("http://") || domainUrl.StartsWith("https://")))
        {
            domainUrl = "https://" + domainUrl;
        }

        _baseUri = new Uri(domainUrl);

        string hubUrl = "http://localhost:4444/wd/hub";
        var pageHandler = new SeleniumPageHandler(BrowserType.Firefox, hubUrl, 1);

        await _robotsService.LoadRobotTxt(domainUrl, _httpClient, cancellationToken);

        if (!_robotsService.IsAllowed(_baseUri.LocalPath, _crawlSettings.UserAgent))
        {
            Log.Information("Robot disallows checking {Url} stopping company check.", domainUrl);
            pageHandler.Quit();
            return new CompanyCheckResult(false, -5, [CompanyCheckReason.RobotRefusal.ToString()]);
        }

        var html = await pageHandler.FetchPageAsync(domainUrl, 0, cancellationToken);
        var text = await pageHandler.ExecuteJavascript("return document.body.innerText;", 0, cancellationToken);

        pageHandler.Quit();

        if (string.IsNullOrEmpty(html))
        {
            Log.Information("Failed to parse {Url}", domainUrl);
            return new CompanyCheckResult(false, 0, [CompanyCheckReason.ParseFailure.ToString()]);
        }

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        CheckCompanyPages(doc);
        CheckFooter(doc);
        CheckStructuredJson(doc);
        CheckIfParkedOrPlaceHolder(text);
        //TODO figure out if need more checks
        //TODO figure out good score values and also what score classifies the confidence
        //i.e. score of 70: Strong company indication
        //              40-69: Likely company
        //              20-39: Uncertain
        //              0-19: unlikely company
        //              < 0 : Strong non-company indication

        if (_score <= 5)
        {
            Log.Information("Score {Score} too low to be sure that {Url} is company.", _score, _baseUri);
            _reasons.Insert(0, CompanyCheckReason.ConfidenceScoreTooLow.ToString());
        } else if (_score > 5 && _score < 10)
        {
            Log.Information("Score {Score} high enough to determine that {Url} might be a company.", _score, _baseUri);
        } else
        {
            Log.Information("Score {Score} high enough to be decently sure that {Url} is most likely a company.", _score, _baseUri);
            _reasons.Add(CompanyCheckReason.CompanyScoreThreshold.ToString());
        }

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
            Log.Information("Found no footers.");
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
            if (footer.Contains("B.V.", StringComparison.InvariantCultureIgnoreCase) ||
                footer.Contains("N.V.", StringComparison.InvariantCultureIgnoreCase) ||
                footer.Contains("Ltd.", StringComparison.InvariantCultureIgnoreCase) ||
                footer.Contains("V.O.F.", StringComparison.InvariantCultureIgnoreCase) ||
                footer.Contains("Inc", StringComparison.InvariantCultureIgnoreCase) ||
                footer.Contains("GmbH", StringComparison.InvariantCultureIgnoreCase) ||
                footer.Contains("©", StringComparison.InvariantCultureIgnoreCase))
            {
                Log.Information("Found {Text} as a legal entity indicator", footer);
                footerScore += 3;
            }

            //Kvk number
            if (footer.Contains("KvK", StringComparison.InvariantCultureIgnoreCase) ||
                footer.Contains("BTW", StringComparison.InvariantCultureIgnoreCase) ||
                footer.Contains("VAT", StringComparison.InvariantCultureIgnoreCase))
            {
                Log.Information("Found {Text} as a company indicator", footer);
                footerScore += 5;
            }

            if (
                footer.Contains("commerce", StringComparison.InvariantCultureIgnoreCase) ||
                footer.Contains("customer", StringComparison.InvariantCultureIgnoreCase) ||
                footer.Contains("subscription", StringComparison.InvariantCultureIgnoreCase) ||
                footer.Contains("shipping", StringComparison.InvariantCultureIgnoreCase)
                )
            {
                Log.Information("Found {Text} as a company indicator", footer);
                footerScore += 1;
            }
        }

        Log.Information("Footer contained {Count} company indicators", footerScore);
        if (footerScore > 0)
        {
            _reasons.Add(CompanyCheckReason.CompanyFooterCheck.ToString());
        }
        _score += Math.Min(footerScore, 5);
    }

    //This checks if the url contains JSON-LD containing schema.org organization etc stuff
    private void CheckStructuredJson(HtmlDocument doc)
    {
        Log.Information("Checking for schema.org structured json.");
        if (doc.DocumentNode == null)
        {
            Log.Information("Failed to check shema.org structured json because document is corrupted.");
            return;
        }

        var nodes = doc.DocumentNode.SelectNodes("//script[@type='application/ld+json']");

        if (nodes == null || nodes.Count == 0)
        {
            Log.Information("Found no schema.org.");
            return;
        }

        var scripts = nodes.Select(script => script.InnerText);

        var schemas = _schemaParser.Parse(scripts);

        var schemaScore = 0;

        bool organizationChecked = false;

        foreach (var schema in schemas)
        {
            if (StructuredCompanyData.IsCompanyType(schema.SchemaType))
            {
                Log.Information("Found {Type} as high company indicator", schema.SchemaType);
                schemaScore += 10;
                break;
            }
            if (StructuredCompanyData.IsMaybeCompanyType(schema.SchemaType) && !organizationChecked) {
                Log.Information("Found {Type} as minor indicator", schema.SchemaType);
                schemaScore += 1;
                organizationChecked = true;
            }
            if (StructuredCompanyData.IsNotCompanyType(schema.SchemaType))
            {
                Log.Information("Found indication {Type} that website is not a company", schema.SchemaType);
                _score = 0;
                _reasons.Add(CompanyCheckReason.IsNotCompanyIndicatorFound.ToString());
                return;
            }

            if (schema.StreetAddress != null)
            {
                Log.Information("Address found in schema");
                schemaScore += 1;
            }
            if (schema.Email != null && schema.Email.EndsWith("gmail.com", StringComparison.InvariantCultureIgnoreCase))
            {
                Log.Information("g-mail.com for e-mail found. Indicates not a company.");
                schemaScore -= 3;
            }
        }

        Log.Information("Found {Count} indicator score for company in the schema.org json", schemaScore);
        _score += schemaScore;
        if (schemaScore > 1)
        {
            _reasons.Add(CompanyCheckReason.CompanyJsonCheck.ToString());
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
            if (link.Contains("about", StringComparison.InvariantCultureIgnoreCase) ||
                link.Contains("privacy", StringComparison.InvariantCultureIgnoreCase) ||
                link.Contains("contact", StringComparison.InvariantCultureIgnoreCase) ||
                link.Contains("services", StringComparison.InvariantCultureIgnoreCase) ||
                link.Contains("info", StringComparison.InvariantCultureIgnoreCase) ||
                link.Contains("over", StringComparison.InvariantCultureIgnoreCase)
                )
            {
                pageScore += 1;
            }

            //TODO check if there are certain links that would indicate it not being a company?
        }

        Log.Information("Found {Count} company indicator pages", pageScore);

        if (pageScore > 0)
        {
            _reasons.Add(CompanyCheckReason.CompanyPagesCheck.ToString());
        }

        //This so that it doesn't increase the score too much
        _score += Math.Min(pageScore, 3);
    }

    //TODO check what kind of phrases to check and what the negative score should be
    private void CheckIfParkedOrPlaceHolder(string text)
    {
        if (text.Contains("This domain is for sale", StringComparison.InvariantCultureIgnoreCase) ||
            text.Contains("Buy this domain", StringComparison.InvariantCultureIgnoreCase) ||
            text.Contains("Coming soon", StringComparison.InvariantCultureIgnoreCase) ||
            text.Contains("Website under construction", StringComparison.InvariantCultureIgnoreCase) ||
            text.Contains("Welcome to nginx", StringComparison.InvariantCultureIgnoreCase) ||
            text.Contains("Domain Parking", StringComparison.InvariantCultureIgnoreCase)
            )
        {
            Log.Information("Placeholder or parking indicator found");
            _score -= 10;
            _reasons.Add(CompanyCheckReason.ParkingIndicator.ToString());
        }
        //TODO probably for parking/placeholder sites can just stop checking the rest?
    }
}
