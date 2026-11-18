using HtmlAgilityPack;
using OpenQA.Selenium;
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

    public async Task<CompanyCheckResult> CheckCompanyAsync(string domainUrl, IWebDriver driver, CancellationToken cancellationToken)
    {
        _score = 0;
        _reasons.Clear();
        _robotsService.Reset();

        Log.Information("Performing preliminary company check for: {Url}", domainUrl);

        if (!(domainUrl.StartsWith("http://") || domainUrl.StartsWith("https://")))
        {
            domainUrl = "https://" + domainUrl;
        }

        _baseUri = new Uri(domainUrl);

        var pageHandler = new SeleniumPageHandler(driver);

        try
        {
            await _robotsService.LoadRobotTxt(domainUrl, _httpClient, cancellationToken);
        }
        catch (Exception ex)
        {
            Log.Information("Failed to load robot.txt: {Exception}", ex.Message);
            return new CompanyCheckResult(false, -5, [CompanyCheckReason.RobotRefusal.ToString()]);
        }

        if (!_robotsService.IsAllowed(_baseUri.LocalPath, _crawlSettings.UserAgent))
        {
            Log.Information("Robot disallows checking {Url} stopping company check.", domainUrl);
            return new CompanyCheckResult(false, -5, [CompanyCheckReason.RobotRefusal.ToString()]);
        }

        string html;
        string text;
        try
        {
            html = await pageHandler.FetchPageAsync(domainUrl, cancellationToken);
            text = await pageHandler.ExecuteJavascript("return document.body.innerText;", cancellationToken);
        }
        catch (OperationCanceledException oex)
        {
            Log.Information("Request was cancelled: {Exception}", oex.Message);
            return new CompanyCheckResult(false, 0, [CompanyCheckReason.CancelRequested.ToString()]);
        }
        catch (Exception ex)
        {
            Log.Information("Failed to fetch page: {Exception}", ex.Message);
            return new CompanyCheckResult(false, 0, [CompanyCheckReason.ParseFailure.ToString()]);
        }

        if (string.IsNullOrEmpty(html))
        {
            Log.Information("Failed to parse {Url}", domainUrl);
            return new CompanyCheckResult(false, 0, [CompanyCheckReason.ParseFailure.ToString()]);
        }

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        bool parked = CheckIfParkedOrPlaceHolder(text);
        if (!parked)
        {
            CheckCompanyPages(doc);
            CheckFooter(doc);
            CheckStructuredJson(doc);
        }

        if (_score <= 5)
        {
            Log.Information("Score {Score} too low to be sure that {Url} is company.", _score, _baseUri);
            _reasons.Insert(0, CompanyCheckReason.ConfidenceScoreTooLow.ToString());
        }
        else if (_score > 5 && _score < 10)
        {
            Log.Information("Score {Score} high enough to determine that {Url} might be a company.", _score, _baseUri);
            _reasons.Insert(0, CompanyCheckReason.ConfidenceScoreMedium.ToString());
        }
        else
        {
            Log.Information("Score {Score} high enough to be decently sure that {Url} is most likely a company.", _score, _baseUri);
            _reasons.Add(CompanyCheckReason.ConfidenceScoreHigh.ToString());
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

        var footerCompanyIndicators = 0;
        var footerNotCompanyIndicators = 0;

        List<string> companyWords = ["commerce", "customer", "subscription", "shipping", "enterprise"];
        List<string> nonCompanyWords = ["wordpress", "charity"];

        foreach (var footer in footerText)
        {
            if (footer.Contains("B.V.", StringComparison.InvariantCultureIgnoreCase) ||
                footer.Contains("N.V.", StringComparison.InvariantCultureIgnoreCase) ||
                footer.Contains("Ltd.", StringComparison.InvariantCultureIgnoreCase) ||
                footer.Contains("V.O.F.", StringComparison.InvariantCultureIgnoreCase) ||
                footer.Contains("Inc", StringComparison.InvariantCultureIgnoreCase) ||
                footer.Contains("GmbH", StringComparison.InvariantCultureIgnoreCase))
            {
                _score += 3;
                footerCompanyIndicators += 1;
            }
            if (footer.Contains("KvK", StringComparison.InvariantCultureIgnoreCase) ||
                footer.Contains("BTW", StringComparison.InvariantCultureIgnoreCase) ||
                footer.Contains("VAT", StringComparison.InvariantCultureIgnoreCase))
            {
                _score += 5;
                footerCompanyIndicators += 1;
            }
            foreach (var companyWord in companyWords)
            {
                if (footer.Contains(companyWord, StringComparison.InvariantCultureIgnoreCase))
                {
                    _score += 2;
                    footerCompanyIndicators += 1;
                }
            }

            foreach (var nonCompanyWord in nonCompanyWords)
            {
                if (footer.Contains(nonCompanyWord, StringComparison.InvariantCultureIgnoreCase))
                {
                    _score -= 2;
                    footerNotCompanyIndicators += 1;
                }
            }
        }

        Log.Information("Footer contained {Count} company indicators and {NotCount} NonCompany indicators", footerCompanyIndicators, footerNotCompanyIndicators);
        if (footerCompanyIndicators > 0 && footerCompanyIndicators >= footerNotCompanyIndicators)
        {
            _reasons.Add(CompanyCheckReason.CompanyFooterIndicationFound.ToString());
        }
        if (footerNotCompanyIndicators > 0)
        {
            _reasons.Add(CompanyCheckReason.NotCompanyFooterIndicationFound.ToString());
        }
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

        var schemaCompanyIndicators = 0;
        var schemaNonCompanyIndicators = 0;

        bool organizationChecked = false;
        bool isNotCompanyChecked = false;

        foreach (var schema in schemas)
        {
            if (StructuredCompanyData.IsCompanyType(schema.SchemaType))
            {
                _score += 10;
                schemaCompanyIndicators += 1;
            }
            if (StructuredCompanyData.IsMaybeCompanyType(schema.SchemaType) && !organizationChecked)
            {
                schemaCompanyIndicators += 1;
                _score += 2;
                organizationChecked = true;
            }
            if (schema.StreetAddress != null)
            {
                _score += 1;
                schemaCompanyIndicators += 1;
            }

            if (StructuredCompanyData.IsNotCompanyType(schema.SchemaType) && !isNotCompanyChecked)
            {
                _score -= 10;
                schemaNonCompanyIndicators += 1;
                isNotCompanyChecked = true;
            }
            if (schema.Email != null && schema.Email.EndsWith("gmail.com", StringComparison.InvariantCultureIgnoreCase))
            {
                _score -= 3;
                schemaNonCompanyIndicators += 1;
            }
        }

        Log.Information("Found {Count} company indicators and {NonCount} non company indicators in the schema.org json", schemaCompanyIndicators, schemaNonCompanyIndicators);
        if (schemaCompanyIndicators > 0 && schemaCompanyIndicators >= schemaNonCompanyIndicators)
        {
            _reasons.Add(CompanyCheckReason.CompanyJsonIndicationFound.ToString());
        }
        else if (schemaNonCompanyIndicators > 0)
        {
            _reasons.Add(CompanyCheckReason.NotCompanyJsonIndicationFound.ToString());
        }
    }


    //This checks if there are /about /company /contact /legal /privacy etc pages
    private void CheckCompanyPages(HtmlDocument doc)
    {
        Log.Information("Checking internal links for company like pages");
        var internalLinks = _htmlExtractor.ExtractInternalLinks(_baseUri!, doc);

        var companyPageIndicators = 0;
        var nonCompanyPageIndicators = 0;

        foreach (var link in internalLinks)
        {
            if (link.Contains("about", StringComparison.InvariantCultureIgnoreCase) ||
                link.Contains("privacy", StringComparison.InvariantCultureIgnoreCase) ||
                link.Contains("contact", StringComparison.InvariantCultureIgnoreCase) ||
                link.Contains("services", StringComparison.InvariantCultureIgnoreCase) ||
                link.Contains("info", StringComparison.InvariantCultureIgnoreCase) ||
                link.Contains("over", StringComparison.InvariantCultureIgnoreCase) ||
                link.Contains("checkout", StringComparison.InvariantCultureIgnoreCase) ||
                link.Contains("cart", StringComparison.InvariantCultureIgnoreCase) ||
                link.Contains("winkelwagen", StringComparison.InvariantCultureIgnoreCase) ||
                link.Contains("shop", StringComparison.InvariantCultureIgnoreCase) ||
                link.Contains("product", StringComparison.InvariantCultureIgnoreCase)
                )
            {
                companyPageIndicators += 1;
                _score += 1;
            }
            if (link.Contains("resume", StringComparison.InvariantCultureIgnoreCase) ||
                link.Contains("cv", StringComparison.InvariantCultureIgnoreCase) ||
                link.Contains("about-me", StringComparison.InvariantCultureIgnoreCase) ||
                link.Contains("blog", StringComparison.InvariantCultureIgnoreCase)
                )
            {
                nonCompanyPageIndicators += 1;
                _score -= 1;
            }
        }

        Log.Information("Found {Count} company indicator pages and {NonCount} non company indicator pages", companyPageIndicators, nonCompanyPageIndicators);

        if (companyPageIndicators > 0 && companyPageIndicators >= nonCompanyPageIndicators)
        {
            _reasons.Add(CompanyCheckReason.CompanyPagesIndicationFound.ToString());
        }
        else if (nonCompanyPageIndicators > 0)
        {
            _reasons.Add(CompanyCheckReason.NotCompanyPagesIndicationFound.ToString());
        }
    }

    private bool CheckIfParkedOrPlaceHolder(string text)
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
            _score -= 20;
            _reasons.Add(CompanyCheckReason.ParkingIndicatorFound.ToString());
            return true;
        }
        return false;
    }
}
