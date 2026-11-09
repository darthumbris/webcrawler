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

app.MapPost("/company", async (CrawlRequest request, CancellationToken cancellationToken) => {
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
    Log.Information("Post endpoint trying domain: {Url}", request.DomainName);
    if (string.IsNullOrWhiteSpace(request.DomainName))
        return Results.BadRequest("domainName is required.");

    var result = await crawler.CrawlAsync(
        request.DomainName,
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

//curl command: curl -X POST -H "Content-Type: application/json" -d "{ \"DomainName\": \"www.coppermine.nl\" }" https://localhost:7299/crawl


//TODO 1. company check doen , welke pagina's loaden om te checken of het een bedrijf is. (Dus de prioriy check, zal dan waarschijnlijk niet via sitemap gaan
// of sitemaps filteren eruit op bepaalde keywords) Prioriy misschien via keywords te checken en die alleen als request toevoegen.
// en als duidelijk is dat het geen company is moeten alle overige requests gecancelled worden?
// of is de company check pas op het einde als alle pagina's gescraped zijn?

//TODO misschien ook beter de too many requests handlen (base url al?)


//als robot tegenhoud stoppen
//return de reden

//preliminary company check
//als duidelijk geen company meteen stoppen
//dan returnen reden dat je stopte
//dan later uitgebreide offline check (met llm)


//crawlresult moet dan misschien ook een Responce code hebben


//misschien de crawler een sessie openen met de programma die de checks uitvoert
//en dan aan de hand van de result verder of stoppen met crawlen.



//Rest command om sessie te openen en sluiten
//krijgt een ID
//met die id kan je dan dingen uitvoeren
//
//rest command om company check uit te voeren , moet een result geven of een company is of niet met een reden
//rest command om verder te crawlen , dit geeft de CrawlResult (is dit voor single page of zoals nu is dat ie sitemap en internal links etc gebruikt?)
//rest command om js uit te voeren? (of moet dit in de crawl gedeelte)

//moet dan denk ik bepaalde volgorde inzitten van de commands?
//