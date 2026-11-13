using Serilog;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using WebCrawler.Configuration;
using WebCrawler.Models;

namespace WebCrawler.Services
{
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

                var fetchedHtml = await pageHandler.FetchPageAsync(info.Location.ToString(), info.DriverIndex, cancel);

                var extractedText = await pageHandler.ExecuteJavascript("return document.body.innerText;", info.DriverIndex, cancel);
                var extractedCss = await pageHandler.ExecuteJavascript(cssGetterScript, info.DriverIndex, cancel);
                var extractedJavascript = await pageHandler.ExecuteJavascript(javascriptGetterScript, info.DriverIndex, cancel);

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
                Log.Warning("Request {Url} cancelled", info.Location);
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
            var active = new ConcurrentDictionary<Task<ProcessResult>, ProcessInfo>(_requestOptions.MaxSimultanousRequests, _requestOptions.MaxSimultanousRequests);
            var availableDrivers = new Queue<int>();

            var randomOffset = new Random();
            var requestsHandled = 0;

            for (int i = 0; i < _requestOptions.MaxSimultanousRequests; i++)
            {
                availableDrivers.Enqueue(i);
            }

            while (!active.IsEmpty || !RequestQueue.IsEmpty)
            {
                cancellationToken.ThrowIfCancellationRequested();
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
                            DriverIndex = availableDrivers.Dequeue(),
                        };

                        Log.Information("Request for {Url} starts with a delay of {Time}ms", url, requestDelay);

                        var task = ProcessAsync(processInfo, pageHandler);
                        active.TryAdd(task, processInfo);
                        requestsHandled++;

                        if (active.Count >= _requestOptions.MaxSimultanousRequests)
                        {
                            break;
                        }
                    }
                }

                await Task.WhenAny(active.Keys).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();

                var completedRequests = active.Keys.Where(r => r.IsCompleted);
                foreach (var completed in completedRequests)
                {
                    active.TryRemove(completed, out var processInfo);
                    PendingRequests--;

                    if (completed.IsFaulted)
                    {
                        var exception = completed.Exception;
                        if (exception != null && exception.InnerException != null)
                        {
                            Log.Warning("completed request had an exception: {Msg}", exception);
                            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                        }
                    }

                    availableDrivers.Enqueue(processInfo!.DriverIndex);

                    await responseHandler(completed.Result);
                }
            }


            Log.Information("Processed {Count} requests", requestsHandled);
        }

    }
}
