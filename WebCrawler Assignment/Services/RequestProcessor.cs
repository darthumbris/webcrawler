using Serilog;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using WebCrawler.Configuration;
using WebCrawler.Models;

namespace WebCrawler.Services;

public class RequestProcessor
{
    private ConcurrentQueue<Uri> RequestQueue { get; } = new();
    private readonly RequestOptions _requestOptions = new();
    private readonly string cssGetterScript;
    private readonly string javascriptGetterScript;
    public int PendingRequests = 0;

    public RequestProcessor()
    {
        cssGetterScript = File.ReadAllText("./Scripts/get_all_css.js");
        javascriptGetterScript = File.ReadAllText("./Scripts/get_all_javascript.js");
    }

    public void AddRequest(Uri uri)
    {
        RequestQueue.Enqueue(uri);
        PendingRequests++;
    }

    private async Task<ProcessResult> ProcessAsync(ProcessInfo info, SeleniumPageHandler pageHandler)
    {
        //add delay to task if needed
        if (info.Delay > 0)
        {
            await Task.Delay(((int)info.Delay));
        }

        var startTime = DateTime.Now;
        info.Timer.Start();

        try
        {
            var timeout = new CancellationTokenSource(_requestOptions.RequestTimeOut).Token;
            var cancel = CancellationTokenSource.CreateLinkedTokenSource(info.Cancellation, timeout).Token;

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
            Log.Warning("Request {Url} cancelled elapsed: {Elapsed}ms cancel {Cancel}", info.Location, info.Timer.Elapsed.TotalMilliseconds, info.Cancellation);
            return new ProcessResult
            {
                Location = info.Location,
                StartTime = startTime,
                Delay = info.Delay,
                ElapsedTime = info.Timer.Elapsed,
            };
        }
        catch (Exception ex)
        {
            info.Timer.Stop();

            Log.Warning("Request for {Url} had Exception: {Exception} after {Time}ms", info.Location, ex.Message, info.Timer.ElapsedMilliseconds);

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
        var requestOptionDelay = _requestOptions.RequestDelay.TotalMilliseconds;

        //Only add the extra delay if it is bigger then the CrawlDelay
        if (requestOptionDelay > 0 && requestOptionDelay > requestDelay)
        {
            requestDelay += requestOptionDelay;
        }
        requestDelay += randomOffset.NextDouble() * _requestOptions.RequestDelayOffset.TotalMilliseconds;
        return requestDelay;
    }

    public async Task ProcessAsync(Func<ProcessResult, Task> responseHandler, SeleniumPageHandler pageHandler, TimeSpan crawlDelay, CancellationToken cancellationToken)
    {
        var randomOffset = new Random();
        var requestsHandled = 0;

        while (!RequestQueue.IsEmpty)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (RequestQueue.TryDequeue(out var url))
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

                var task = ProcessAsync(processInfo, pageHandler);
                requestsHandled++;

                cancellationToken.ThrowIfCancellationRequested();
                PendingRequests--;
                if (task.IsFaulted)
                {
                    var exception = task.Exception;
                    if (exception != null && exception.InnerException != null)
                    {
                        Log.Warning("completed request for {Url} had an exception: {Msg}", processInfo.Location, exception);
                        ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                    }
                }
                await responseHandler(task.Result);
            }
        }

        Log.Information("Processed {Count} requests", requestsHandled);
    }

}
