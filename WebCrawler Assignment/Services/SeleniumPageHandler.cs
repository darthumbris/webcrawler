namespace WebCrawler.Services;

using OpenQA.Selenium;
using WebCrawler.Configuration;
using WebCrawler.Models;

public sealed record FetchedSiteMap
(
    IReadOnlyCollection<Uri> Links,
    IReadOnlyCollection<Uri> SiteMaps,
    Uri Url
);

//TODO maybe don't change the LastActivity here, RequestOngoing already protects it from getting closed
//TODO check how to use the PinnedScript for the executescript
public sealed class SeleniumPageHandler(SessionEntry session)
{
    private readonly SessionEntry _session = session;
    private readonly IWebDriver _driver = session.Driver;
    private readonly RequestOptions _requestOptions = new();
    private readonly IJavaScriptExecutor _executor = (IJavaScriptExecutor)session.Driver;

    private async Task ScrollPage(CancellationToken cancellationToken)
    {
        var scrollsPerformed = 0;
        var lastHeight = _executor.ExecuteScript("return document.body.scrollHeight");

        while (scrollsPerformed < _requestOptions.MaxScrolls)
        {
            _session.LastActivity = DateTimeOffset.UtcNow;
            _executor.ExecuteScript("window.scrollTo(0, document.body.scrollHeight);");
            await Task.Delay(_requestOptions.ScrollDelay, cancellationToken);

            var newHeight = _executor.ExecuteScript("return document.body.scrollHeight");
            if (newHeight != null && newHeight.Equals(lastHeight))
            {
                break;
            }
            lastHeight = newHeight;
            scrollsPerformed += 1;
        }
    }

    public async Task<string> FetchPageAsync(string url, CancellationToken cancellationToken)
    {
        _session.LastActivity = DateTimeOffset.UtcNow;
        cancellationToken.ThrowIfCancellationRequested();
        _driver.Navigate().GoToUrl(url);
        await ScrollPage(cancellationToken);

        //really simple check for 429 Too many Requests
        //because Selenium doesn't give response codes for some stupid reason....
        if (_driver.PageSource.Contains("<h1>Too Many Requests</h1>"))
        {
            throw new Exception("Too many request response detected");
        }

        return _driver.PageSource;
    }

    public async Task<string> ExecuteJavascriptAsync(string javascript, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return (string?)_executor.ExecuteAsyncScript(javascript) ?? string.Empty;
    }

    public async Task<string> ExecuteJavascript(string javascript, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _session.LastActivity = DateTimeOffset.UtcNow;
        return (string?)_executor.ExecuteScript(javascript) ?? string.Empty;
    }
}