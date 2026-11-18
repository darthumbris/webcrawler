using Serilog;
using WebCrawler.Models;
using WebCrawler.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddScoped<ICrawlService, CrawlService>();

var companyChecker = new CompanyChecker();
var seleniumSessionService = new SeleniumSessionService();

Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();

builder.Host.UseSerilog();
var app = builder.Build();

app.MapPost("/company", async (CompanyRequest request, CancellationToken cancellationToken) =>
{
    Log.Information("Company Check end point.");
    if (string.IsNullOrWhiteSpace(request.DomainName))
        return Results.BadRequest("domainName is required.");

    var driver = seleniumSessionService.GetSession(request.Uuid);
    if (driver == null)
    {
        return Results.InternalServerError("Failed to get session");
    }
    var result = await companyChecker.CheckCompanyAsync(request.DomainName, driver, cancellationToken);

    return Results.Ok(result);
});

app.MapPost("/crawl", async (
    CrawlRequest request,
    ICrawlService crawler,
    CancellationToken cancellationToken) =>
{
    Log.Information("Crawl endpoint trying domain: {Url}", request.DomainName);
    if (string.IsNullOrWhiteSpace(request.DomainName))
        return Results.BadRequest("domainName is required.");

    var driver = seleniumSessionService.GetSession(request.Uuid);

    if (driver == null)
    {
        return Results.InternalServerError("Failed to get session");
    }
    //the CompanyCheck is so that the crawler only crawls pages using a filter
    //These crawled pages can then be used by the next check to
    //see if the url is a company
    //if it is it can then do the /crawl with the CompanyCheck = false
    //this way the crawler will crawl all pages
    var result = await crawler.CrawlAsync(
        request.DomainName,
        request.CompanyCheck,
        request.UseSitemaps ?? false,
        driver,
        cancellationToken);

    return Results.Ok(result);
});

app.MapPost("/open", async (SessionOpenRequest request) =>
{
    Log.Information("open endpoint request: {Request}", request.ToString());
    var uid = seleniumSessionService.OpenSessionRemote(request.BrowserType);

    if (uid != null)
    {
        return Results.Ok(new SessionOpenResult(uid.ToString(), true));
    }
    else
    {
        return Results.InternalServerError("Failed to open session");
    }
});

app.MapPost("/close", async (SessionCloseRequest request) =>
{
    if (seleniumSessionService.CloseSession(request.Uuid))
    {
        return Results.Ok(new SessionCloseResult(true));
    }
    else
    {
        return Results.InternalServerError("Failed to close session");
    }
});

app.MapGet("/health", () =>
{
    return Results.Ok(new
    {
        status = "healthy",
        service = "webcrawler",
        timestamp = DateTimeOffset.UtcNow,
    });
});

app.Run();

//curl commands:
//open sessie:      curl -X POST -H "Content-Type: application/json" -d "{ \"BrowserType\": 2}" https://localhost:7299/open

//close sessie:     curl -X POST -H "Content-Type: application/json" -d "{ \"Uuid\": \"\"}" https://localhost:7299/close

//crawl:            curl -X POST -H "Content-Type: application/json" -d "{ \"DomainName\": \"www.coppermine.nl\", \"Uuid\": \"\", \"CompanyCheck\": false, \"UseSitemaps\": false }" https://localhost:7299/crawl

//company:          curl -X POST -H "Content-Type: application/json" -d "{ \"DomainName\": \"www.coppermine.nl\", \"Uuid\": \"\" }" https://localhost:7299/company

//rest command om verder te crawlen , dit geeft de CrawlResult (is dit voor single page of zoals nu is dat ie sitemap en internal links etc gebruikt?)
//rest command om js uit te voeren? (of moet dit in de crawl gedeelte)

//moet dan denk ik bepaalde volgorde inzitten van de commands?
// 0. sessie openen (/open)
// 1. preliminary company check (/company)
// 2. crawl misschien bepaalde pagina's (/crawl met CompanyCheck = true)
// 3. LLM check of company is 
// 4. Crawl de rest van de site (/crawl met CompanyCheck = false)
// 5. sessie sluiten (/close)


//TODO
//1. na crawlen van website de sessie laten sluiten (Of moet dit alleen via de /close endpoint ?)
//2. na timeout moet de sessie gestopt worden (15 min) (Dit kan in de dockerfile gezet worden) (of moet de sessie handmatig worden ge quit en dan uit de session service gehaald worden?)
//3. error handling per sessie handlen (dus catchen van timeout en crashes etc)

//TODO Voor selenium grid kijk ook naar de Distributed Mode
//hier je een router, distributor en nodes die daarmee connecten die allemaal
//op verschillende machines draaien
//op die manier kan je veel meer parallel alles checken