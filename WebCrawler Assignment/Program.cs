using Serilog;
using Serilog.Context;
using Serilog.Templates;
using Serilog.Templates.Themes;
using System.Net;
using WebCrawler.Configuration;
using WebCrawler.Models;
using WebCrawler.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services
    .AddOptions<CrawlerSettings>()
    .Bind(builder.Configuration.GetSection("Crawler"))
    .Validate(settings => settings.MaxPages > 0,
        "MaxPages must be greater than zero.")
    .Validate(settings => settings.MaxPagesCompanyCheck > 0,
        "MaxPagesCompanyCheck must be greater than zero.")
    .Validate(settings => settings.MaxRequestRetries >= 0,
        "MaxRequestRetries cannot be negative.")
    .Validate(settings => settings.MaxSitemapDecompressedBytes > 0,
    "MaxSitemapDecompressedBytes must be greater than zero.")
    .Validate(settings => settings.MaxRobotsBytes > 0,
    "MaxRobotsBytes must be greater than zero.")
    .Validate(settings => settings.MaxChromeSessions > 0,
        "MaxChromeSessions must be greater than zero.")
    .Validate(settings => settings.MaxFirefoxSessions > 0,
        "MaxFirefoxSessions must be greater than zero.")
    .Validate(settings => settings.MaxEdgeSessions > 0,
        "MaxEdgeSessions must be greater than zero.")
    .Validate(settings => settings.MaxSitemaps > 0, "MaxSitemaps must be greater than zero.")
    .Validate(settings => settings.MaxSitemapLinks > 0, "MaxSitemapLinks must be greater than zero.")
    .Validate(settings => settings.MaxSitemapIndices > 0, "MaxSitemapIndices must be greater than zero.")
    .Validate(settings => settings.MaxSitemapTotalLinks > 0, "MaxSitemapTotalLinks must be greater than zero.")
    .Validate(settings => settings.MaxSitemapDepth >= 0, "MaxSitemapDepth cannot be negative.")
    .Validate(settings => settings.PageLoadTimeOut > TimeSpan.Zero, "PageLoadTimeOut must be greater than zero.")
    .Validate(settings => settings.SessionTimeOut > TimeSpan.Zero, "SessionTimeOut must be greater than zero.")
    .Validate(settings => !string.IsNullOrEmpty(settings.HubUrl), "HubUrl can't be empty.")
    .Validate(settings => !string.IsNullOrEmpty(settings.UserAgent), "UserAgent can't be empty.")
    .ValidateOnStart();
builder.Services.AddOptions<RequestOptions>()
    .Bind(builder.Configuration.GetSection("Requests"))
    .Validate(settings => settings.RequestDelay >= TimeSpan.Zero, "Delay cannot be negative.")
    .Validate(settings => settings.RequestDelayOffset >= TimeSpan.Zero, "DelayOffset cannot be negative.")
    .Validate(settings => settings.ScrollDelay >= TimeSpan.Zero, "ScrollDelay cannot be negative.")
    .Validate(settings => settings.RequestTimeOut > TimeSpan.Zero, "RequestTimeout must be greater than zero.")
    .Validate(settings => settings.MaxScrolls > 0, "MaxScrolls must be greater than zero.")
    .ValidateOnStart();
builder.Services.AddSingleton<SeleniumSessionService>();
builder.Services.AddHostedService<SessionCleanupService>();
builder.Services.AddScoped<ICrawlService, CrawlService>();
builder.Services.AddScoped<ICompanyChecker, CompanyChecker>();

//the HttpClient have these userAgents so that the robots.txt and sitemaps can be loaded properly
//selenium will use the correct userAgent
builder.Services.AddHttpClient("Crawler", client =>
{
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:159.0) Gecko/20100101 Firefox/159.0");
});
builder.Services.AddHttpClient("Sitemap", client =>
{
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:159.0) Gecko/20100101 Firefox/159.0");
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    AllowAutoRedirect = false,
    AutomaticDecompression =
        DecompressionMethods.GZip |
        DecompressionMethods.Deflate |
        DecompressionMethods.Brotli
});

Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .WriteTo.Console(
    new ExpressionTemplate(
        "[{@t:HH:mm:ss} {@l:u3}] " +
        "{#if SessionId is not null}[{SessionId}] {#end}" +
        "{@m}\n{@x}", theme: TemplateTheme.Literate)
    )
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
    using (LogContext.PushProperty("SessionId", request.Uuid))
    {
        Log.Information("Company check end point.");
        if (string.IsNullOrWhiteSpace(request.DomainName))
            return Results.BadRequest("domainName is required.");

        if (!Uri.TryCreate(request.DomainName, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return Results.BadRequest(
                "DomainName must be a valid HTTP or HTTPS URL.");
        }

        var (session, response) = sessions.BeginRequest(request.Uuid);
        switch (response)
        {
            case SessionResponse.SessionFound:
                break;
            case SessionResponse.SessionNotFound:
                return Results.NotFound("Session not found");
            case SessionResponse.GuidParseFailure:
                return Results.BadRequest("The uuid is not valid.");
            case SessionResponse.SessionRequestOngoing:
                return Results.Conflict("This session is already processing a request.");
            default:
                return Results.InternalServerError($"Company request failed: {response}");
        }

        try
        {
            var result = await companyChecker.CheckCompanyAsync(request.DomainName, session!, cancellationToken);
            return Results.Ok(result);
        }
        finally
        {
            if (cancellationToken.IsCancellationRequested || session?.DriverInvalidated == true)
            {
                sessions.AbortRequest(request.Uuid);
            }
            else
            {
                sessions.EndRequest(request.Uuid);
            }
        }
    }
});

app.MapPost("/crawl", async (
    CrawlRequest request,
    ICrawlService crawler,
    SeleniumSessionService sessions,
    CancellationToken cancellationToken) =>
{
    using (LogContext.PushProperty("SessionId", request.Uuid))
    {
        Log.Information("Crawl endpoint trying domain: {Url}", request.DomainName);
        if (string.IsNullOrWhiteSpace(request.DomainName))
            return Results.BadRequest("domainName is required.");

        if (!Uri.TryCreate(request.DomainName, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return Results.BadRequest(
                "DomainName must be a valid HTTP or HTTPS URL.");
        }

        var (session, response) = sessions.BeginRequest(request.Uuid);

        switch (response)
        {
            case SessionResponse.SessionFound:
                break;
            case SessionResponse.SessionNotFound:
                return Results.NotFound("Session not found");
            case SessionResponse.GuidParseFailure:
                return Results.BadRequest("The uuid is not valid.");
            case SessionResponse.SessionRequestOngoing:
                return Results.Conflict("This session is already processing a request.");
            default:
                return Results.InternalServerError($"Crawl request failed: {response}");
        }



        try
        {
            var result = await crawler.CrawlAsync(
                request.DomainName,
                request.CompanyCheck,
                request.UseSitemaps ?? false,
                session!,
                cancellationToken);

            return Results.Ok(result);
        }
        finally
        {
            if (cancellationToken.IsCancellationRequested || session?.DriverInvalidated == true)
            {
                sessions.AbortRequest(request.Uuid);
            }
            else
            {
                sessions.EndRequest(request.Uuid);
            }
        }
    }
});

app.MapPost("/open", (SessionOpenRequest request, SeleniumSessionService sessions) =>
{
    var (uid, response) = sessions.OpenSessionRemote(request.BrowserType);

    return response switch
    {
        SessionResponse.SessionCreatedSuccessfully => Results.Ok(new SessionOpenResult(uid!.ToString(), true)),
        SessionResponse.SessionLimitReached => TypedResults.Problem(title: "Session Limit Reached", statusCode: StatusCodes.Status503ServiceUnavailable),
        SessionResponse.UnsupportedBrowserType => Results.BadRequest("Browsertype not supported"),
        _ => Results.InternalServerError($"Failed to open session: {response}"),
    };
});

app.MapPost("/close", (SessionCloseRequest request, SeleniumSessionService sessions) =>
{
    using (LogContext.PushProperty("SessionId", request.Uuid))
    {
        var closeResponse = sessions.CloseSession(request.Uuid);
        return closeResponse switch
        {
            SessionResponse.SessionClosedSuccessfully => Results.Ok(new SessionCloseResult(true)),
            SessionResponse.SessionNotFound => Results.NotFound("Session not found"),
            SessionResponse.SessionRequestOngoing => Results.Conflict("This session is still processing a request."),
            SessionResponse.GuidParseFailure => Results.BadRequest("The uuid was not valid"),
            _ => Results.InternalServerError($"Failed to close session: {closeResponse}"),
        };
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

//TODO Voor selenium grid kijk ook naar de Distributed Mode
//hier je een router, distributor en nodes die daarmee connecten die allemaal
//op verschillende machines draaien
//op die manier kan je veel meer parallel alles checken