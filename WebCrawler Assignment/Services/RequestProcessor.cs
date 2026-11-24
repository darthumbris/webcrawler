using Microsoft.Extensions.Options;
using OpenQA.Selenium;
using Serilog;
using System.Collections.Concurrent;
using System.Diagnostics;
using WebCrawler.Configuration;
using WebCrawler.Models;

namespace WebCrawler.Services;

public class RequestProcessor
{
    private readonly ConcurrentQueue<Uri> _requestQueue = new();
    public readonly RequestOptions RequestOptions;
    private readonly string cssGetterScript;
    private readonly string javascriptGetterScript;
    private int _pendingRequests;

    public int PendingRequests =>
        Volatile.Read(ref _pendingRequests);

    private static string ReadEmbeddedScript(string fileName)
    {
        var resourceName = $"WebCrawler.Scripts.{fileName}";
        var assembly = typeof(RequestProcessor).Assembly;

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Embedded script resource was not found: {resourceName}");

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public RequestProcessor(IOptions<RequestOptions> requestOptions)
    {
        RequestOptions = requestOptions.Value;
        cssGetterScript = ReadEmbeddedScript("get_all_css.js");
        javascriptGetterScript = ReadEmbeddedScript("get_all_javascript.js");
    }

    public void AddRequest(Uri uri)
    {
        Interlocked.Increment(ref _pendingRequests);
        _requestQueue.Enqueue(uri);
    }

    private async Task<ProcessResult> ProcessAsync(ProcessInfo info, SeleniumPageHandler pageHandler)
    {
        //add delay to task if needed
        if (info.Delay > 0)
        {
            await Task.Delay(((int)info.Delay), info.Cancellation);
        }

        var startTime = DateTime.UtcNow;
        info.Timer.Start();
        using var timeout = new CancellationTokenSource(RequestOptions.RequestTimeOut);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(info.Cancellation, timeout.Token);
        var cancel = linkedSource.Token;

        try
        {
            var fetchedHtml = await pageHandler.FetchPageAsync(info.Location.ToString(), cancel);
            var extractedText = await pageHandler.ExecuteJavascript("return document.body.innerText;", cancel);
            var extractedCss = await pageHandler.ExecuteJavascript(cssGetterScript, cancel);
            var extractedJavascript = await pageHandler.ExecuteJavascript(javascriptGetterScript, cancel);

            info.Timer.Stop();
            info.Cancellation.ThrowIfCancellationRequested();

            Log.Information("Request for {Url} completed in {Time}ms", info.Location, info.Timer.ElapsedMilliseconds);

            return new ProcessResult
            {
                Location = info.Location,
                StartTime = startTime,
                Delay = info.Delay,
                HtmlContent = fetchedHtml,
                ElapsedTime = info.Timer.Elapsed,
                TextContent = extractedText,
                CssContent = extractedCss,
                JavascriptContent = extractedJavascript
            };
        }
        catch (OperationCanceledException) when (info.Cancellation.IsCancellationRequested)
        {
            info.Timer.Stop();
            Log.Warning("The entire crawl was cancelled for {Url}", info.Location);
            throw;
        }
        catch (OperationCanceledException ex) when (timeout.IsCancellationRequested)
        {
            info.Timer.Stop();
            Log.Warning("The request timeout expired for {Url}", info.Location);
            throw new SeleniumAbortedException(
                $"The page request timed out for {info.Location}.", ex);
        }
        catch (SeleniumAbortedException)
        {
            info.Timer.Stop();
            throw;
        }
        catch (Exception ex)
        {
            info.Timer.Stop();

            Log.Warning(ex, "Request for {Url} had Exception after {Time}ms", info.Location, info.Timer.ElapsedMilliseconds);

            return new ProcessResult
            {
                Location = info.Location,
                StartTime = startTime,
                Delay = info.Delay,
                ElapsedTime = info.Timer.Elapsed,
                Exception = ex
            };
        }
    }

    private double Delay(TimeSpan crawlDelay, Random randomOffset)
    {
        var requestDelay = crawlDelay.TotalMilliseconds;
        var requestOptionDelay = RequestOptions.RequestDelay.TotalMilliseconds;

        //Only add the extra delay if it is bigger then the CrawlDelay
        if (requestOptionDelay > 0 && requestOptionDelay > requestDelay)
        {
            requestDelay += requestOptionDelay;
        }
        requestDelay += randomOffset.NextDouble() * RequestOptions.RequestDelayOffset.TotalMilliseconds;
        return requestDelay;
    }

    public async Task ProcessAsync(Func<ProcessResult, Task> responseHandler, SeleniumPageHandler pageHandler, TimeSpan crawlDelay, CancellationToken cancellationToken)
    {
        var randomOffset = new Random();
        var requestsHandled = 0;

        while (!_requestQueue.IsEmpty)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_requestQueue.TryDequeue(out var url))
            {
                var requestDelay = Delay(crawlDelay, randomOffset);
                var processInfo = new ProcessInfo
                {
                    Location = url,
                    Timer = new Stopwatch(),
                    Delay = requestDelay,
                    Cancellation = cancellationToken,
                };

                Log.Information("Request for {Url} starts with a delay of {Time}ms", url, requestDelay);

                try
                {
                    var result = await ProcessAsync(processInfo, pageHandler);
                    requestsHandled++;

                    await responseHandler(result);
                }
                finally
                {
                    Interlocked.Decrement(ref _pendingRequests);
                }
            }
        }

        Log.Information("Processed {Count} requests", requestsHandled);
    }

}
