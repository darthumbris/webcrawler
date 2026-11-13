using Serilog;
using WebCrawler.Models;
using WebCrawler.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddScoped<ICrawlService, CrawlService>();

var companyChecker = new CompanyChecker();

Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();

builder.Host.UseSerilog();
var app = builder.Build();

app.MapPost("/company", async (CrawlRequest request, CancellationToken cancellationToken) =>
{
    Log.Information("Company Check end point.");
    if (string.IsNullOrWhiteSpace(request.DomainName))
        return Results.BadRequest("domainName is required.");

    var result = await companyChecker.CheckCompanyAsync(request.DomainName, cancellationToken);

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

    //the CompanyCheck is so that the crawler only crawls pages using a filter
    //These crawled pages can then be used by the next check to
    //see if the url is a company
    //if it is it can then do the /crawl with the CompanyCheck = false
    //this way the crawler will crawl all pages
    var result = await crawler.CrawlAsync(
        request.DomainName,
        request.CompanyCheck,
        request.UseSitemaps ?? false,
        cancellationToken);

    return Results.Ok(result);
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

//curl command: curl -X POST -H "Content-Type: application/json" -d "{ \"DomainName\": \"www.coppermine.nl\", \"CompanyCheck\": false, \"UseSitemaps\": false }" https://localhost:7299/crawl
//curl command companyCheck: curl -X POST -H "Content-Type: application/json" -d "{ \"DomainName\": \"www.coppermine.nl\", \"CompanyCheck\": true, \"UseSitemaps\": false }" https://localhost:7299/crawl

//TODO misschien ook beter de too many requests handlen (base url al?)

//Rest command om sessie te openen en sluiten
//krijgt een ID
//met die id kan je dan dingen uitvoeren
//
//rest command om company check uit te voeren , moet een result geven of een company is of niet met een reden
//rest command om verder te crawlen , dit geeft de CrawlResult (is dit voor single page of zoals nu is dat ie sitemap en internal links etc gebruikt?)
//rest command om js uit te voeren? (of moet dit in de crawl gedeelte)

//moet dan denk ik bepaalde volgorde inzitten van de commands?
// 0.? Misschien als eerste een robot check?
// 1. preliminary company check
// 2. crawl misschien bepaalde pagina's
// 3. LLM check of company is
// 4. Crawl de rest van de site


//TODO Voor selenium grid kijk ook naar de Distributed Mode
//hier je een router, distributor en nodes die daarmee connecten die allemaal
//op verschillende machines draaien
//op die manier kan je veel meer parallel alles checken