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
        public int PendingRequests = 0;

        public RequestProcessor() { }

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

                //TODO execute the script here to check what variables etc are loaded (ask what the script is)
                //and then need to know what to do with the result of that script

                //this javascript will get all the text from the page, should probably add that in the processResult as well, that way the html extractor doesn't need to handle that.
                //var extractedText = await pageHandler.ExecuteJavascriptAsync("var callback = arguments[arguments.length - 1];var check = () => {callback(document.body.innerText);};check();", cancel);
                var extractedText = await pageHandler.ExecuteJavascript("return document.body.innerText;", info.DriverIndex, cancel);

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
                    TextContent = extractedText
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

        public async Task ProcessAsync(Func<ProcessResult, Task> responseHandler, SeleniumPageHandler pageHandler, CancellationToken cancellationToken = default)
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
                        Double requestDelay = 0;

                        if (_requestOptions.RequestDelay.TotalMilliseconds > 0)
                        {
                            requestDelay = _requestOptions.RequestDelay.TotalMilliseconds;
                            requestDelay += randomOffset.NextDouble() * _requestOptions.RequestDelayOffset.TotalMilliseconds;
                        }

                        var driverIndex = availableDrivers.Dequeue();

                        var processInfo = new ProcessInfo
                        {
                            Location = url,
                            Timer = new Stopwatch(),
                            Delay = requestDelay,
                            Cancellation = cancellationToken,
                            DriverIndex = driverIndex,
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
