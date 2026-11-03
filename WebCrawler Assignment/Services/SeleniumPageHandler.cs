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
    Uri url
);

public sealed class SeleniumPageHandler
{
    private readonly IWebDriver _driver;
    private readonly IJavaScriptExecutor _executor;
    private readonly HttpClient _httpClient;

    public SeleniumPageHandler(IWebDriver driver, HttpClient httpClient)
    {
        _driver = driver;
        _executor = (IJavaScriptExecutor)driver;
        _httpClient = httpClient;
    }

    public async Task<string> FetchPageAsync(
        string url,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _driver.Navigate().GoToUrl(url);

        return _driver.PageSource;
    }

    public async Task<string?> ExecuteJavascriptAsync(string javascript, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = (string?)_executor.ExecuteAsyncScript(javascript);

        return result;
    }

    //TODO check if the Synchrous version of ExecuteScript is needed, if not remove it
    public async Task<string?> ExecuteJavascriptSync(string javascript, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = (string?)_executor.ExecuteScript(javascript);

        return result;
    }

    public void Quit()
    {
        _driver.Quit();
    }
}