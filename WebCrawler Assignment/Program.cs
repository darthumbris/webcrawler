using Serilog;
using WebCrawler.Models;
using WebCrawler.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<SeleniumSessionService>();
builder.Services.AddHostedService<SessionCleanupService>();
builder.Services.AddScoped<ICrawlService, CrawlService>();
builder.Services.AddScoped<ICompanyChecker, CompanyChecker>();
builder.Services.AddHttpClient("Crawler", client =>
{
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:159.0) Gecko/20100101 Firefox/159.0");
});

Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();

builder.Host.UseSerilog();
var app = builder.Build();

app.MapPost("/company", async (
    CompanyRequest request,
    ICompanyChecker companyChecker,
    SeleniumSessionService sessions,
    CancellationToken cancellationToken
    ) =>
{
    Log.Information("Company Check end point.");
    if (string.IsNullOrWhiteSpace(request.DomainName))
        return Results.BadRequest("domainName is required.");

    var session = sessions.BeginRequest(request.Uuid);
    if (session == null)
    {
        return Results.InternalServerError("The session does not exist or is already processing a request.");
    }
    try
    {
        var result = await companyChecker.CheckCompanyAsync(request.DomainName, session, cancellationToken);
        return Results.Ok(result);
    }
    finally
    {
        sessions.EndRequest(request.Uuid);
    }
});

app.MapPost("/crawl", async (
    CrawlRequest request,
    ICrawlService crawler,
    SeleniumSessionService sessions,
    CancellationToken cancellationToken) =>
{
    Log.Information("Crawl endpoint trying domain: {Url}", request.DomainName);
    if (string.IsNullOrWhiteSpace(request.DomainName))
        return Results.BadRequest("domainName is required.");

    var session = sessions.BeginRequest(request.Uuid);

    if (session == null)
    {
        return Results.InternalServerError("The session does not exist or is already processing a request.");
    }

    try
    {
        var result = await crawler.CrawlAsync(
            request.DomainName,
            request.CompanyCheck,
            request.UseSitemaps ?? false,
            session,
            cancellationToken);

        return Results.Ok(result);
    }
    finally
    {
        sessions.EndRequest(request.Uuid);
    }
});

app.MapPost("/open", (SessionOpenRequest request, SeleniumSessionService sessions) =>
{
    Log.Information("open endpoint request: {Request}", request.ToString());
    var uid = sessions.OpenSessionRemote(request.BrowserType);

    if (uid != null)
    {
        return Results.Ok(new SessionOpenResult(uid.ToString(), true));
    }
    else
    {
        return Results.InternalServerError("Failed to open session");
    }
});

app.MapPost("/close", (SessionCloseRequest request, SeleniumSessionService sessions) =>
{
    if (sessions.CloseSession(request.Uuid))
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
    //TODO Kan hier ook laten zien hoeveel open sessions?
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

//TODO
//1. alleen http en https toelaten (Dus domainUrl MOET http:// of https:// bevatten!!!!)

//TODO Voor selenium grid kijk ook naar de Distributed Mode
//hier je een router, distributor en nodes die daarmee connecten die allemaal
//op verschillende machines draaien
//op die manier kan je veel meer parallel alles checken