using Serilog;
using WebCrawler.Models;
using WebCrawler.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddScoped<ICrawlService, CrawlService>();

Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();

builder.Host.UseSerilog();
var app = builder.Build();

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
    //Not sure what to put here?
    //TODO check what to put here
    //maybe if the selenium driver is still running and can fetch a page, if not return unhealthy
    return Results.Ok(new
    {
        status = "healthy"
    });
});

app.Run();

//curl command: curl -X POST -H "Content-Type: application/json" -d "{ \"DomainName\": \"www.coppermine.nl\" }" https://localhost:7299/crawl


//TODO 1. custom javascript to run on the pages to check what variables are set and what is being loaded
//TODO 2. company check doen , welke pagina's loaden om te checken of het een bedrijf is. (Dus de prioriy check, zal dan waarschijnlijk niet via sitemap gaan
// of sitemaps filteren eruit op bepaalde keywords) Prioriy misschien via keywords te checken en die alleen als request toevoegen.
//TODO 3. check how it scales with the selenium hub?
//TODO 4. check how many active selenium drivers can be run at the same time