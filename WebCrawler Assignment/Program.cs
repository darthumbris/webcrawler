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

//CrawlService crawler = new CrawlService();
//Endpoints temporary until proper crawler implementation
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
    //Temporary when actual crawling is implemented that gives the health status
    return Results.Ok(new
    {
        status = "healthy"
    });
});

app.Run();

//TODO implemt the robot.txt (and robot header) handling (which urls are allowed to be crawled and what delay between requests)
//TODO implement the scoring system do determine "interesting" pages

//TODO handle the html extraction

//TODO handle the logging with SeriLog (also check where logging is needed)