namespace WebCrawler.Services;

using OpenQA.Selenium;

public sealed record FetchedPage(
    string Url,
    string Html,
    string? Title);

public sealed record FetchedSiteMap
(
    IReadOnlyCollection<Uri> Links,
    IReadOnlyCollection<Uri> SiteMaps,
    Uri Url
);

public sealed class SeleniumPageHandler(IWebDriver driver)
{
    private readonly IWebDriver _driver = driver;
    private readonly IJavaScriptExecutor _executor = (IJavaScriptExecutor)driver;

    public async Task<string> FetchPageAsync(
        string url,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _driver.Navigate().GoToUrl(url);

        return _driver.PageSource;
    }

    public async Task<string> ExecuteJavascriptAsync(string javascript, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = (string?)_executor.ExecuteAsyncScript(javascript);

        result = result ?? string.Empty;
        return result;
    }

    //TODO check if the Synchrous version of ExecuteScript is needed, if not remove it
    public async Task<string> ExecuteJavascriptSync(string javascript, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = (string?)_executor.ExecuteScript(javascript);
        
        result = result ?? string.Empty;
        return result;
    }

    public void Quit()
    {
        _driver.Quit();
    }
}