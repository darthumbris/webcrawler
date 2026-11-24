namespace WebCrawler.Services;

using OpenQA.Selenium;
using Serilog;
using WebCrawler.Configuration;
using WebCrawler.Models;

public sealed record FetchedSiteMap
(
    IReadOnlyCollection<Uri> Links,
    IReadOnlyCollection<Uri> SiteMaps,
    Uri Url
);

public sealed class SeleniumPageHandler(IWebDriver driver, RequestOptions options)
{
    private readonly IWebDriver _driver = driver;
    private readonly RequestOptions _requestOptions = options;
    private readonly IJavaScriptExecutor _executor = (IJavaScriptExecutor)driver;

    private async Task ScrollPage(CancellationToken cancellationToken)
    {
        var scrollsPerformed = 0;
        var lastHeight = _executor.ExecuteScript("return document.body.scrollHeight");

        while (scrollsPerformed < _requestOptions.MaxScrolls)
        {
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
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _driver.Navigate().GoToUrlAsync(url).WaitAsync(cancellationToken);
            await ScrollPage(cancellationToken);

            //really simple check for 429 Too many Requests
            //because Selenium doesn't give response codes for some stupid reason....
            if (_driver.PageSource.Contains("<h1>Too Many Requests</h1>"))
            {
                throw new Exception("Too many request response detected");
            }
            return _driver.PageSource;
        }
        catch (WebDriverException ex)
        {
            Log.Warning(
                ex,
                "Selenium driver failed while loading {Url}.",
                url);

            throw new SeleniumAbortedException(
                $"Selenium driver failed while loading {url}.",
                ex);
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            Log.Information("Selenium navigation cancelled for {Url}", url);
            throw new SeleniumAbortedException($"Selenium driver was cancelled while loading {url}.", ex);
        }
    }

    public async Task<string> ExecuteJavascript(string javascript, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = (string?)_executor.ExecuteScript(javascript) ?? string.Empty;

            cancellationToken.ThrowIfCancellationRequested();

            return result;
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            throw new SeleniumAbortedException("Selenium javascript execution was cancelled.", ex);
        }
        catch (WebDriverException ex)
        {
            throw new SeleniumAbortedException("Selenium javascript execution failed because of driver failure.", ex);
        }
    }
}