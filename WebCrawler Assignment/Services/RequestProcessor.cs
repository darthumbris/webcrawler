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
        private ConcurrentQueue<Uri> _requestQueue { get; } = new();
        private readonly RequestOptions _requestOptions = new();
        public int PendingRequests = 0;

        public RequestProcessor() { }

        public void AddRequest(Uri uri)
        {
            _requestQueue.Enqueue(uri);
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

                //TODO execute the proper javascript here, maybe need to execute multiple?
                //this javascript will get all the text from the page, should probably add that in the processResult as well, that way the html extractor doesn't need to handle that.
                var extractedText = await pageHandler.ExecuteJavascriptAsync("var callback = arguments[arguments.length - 1];var check = () => {callback(document.body.innerText);};check();", cancel);

                info.Timer.Stop();

                info.Cancellation.ThrowIfCancellationRequested();

                Log.Information("Request for {Url} completed in {ms}ms", info.Location, info.Timer.ElapsedMilliseconds);

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
                Log.Information("Request {Url} cancelled", info.Location);
                return null;
            }
            catch (Exception ex)
            {
                info.Timer.Stop();

                Log.Information("Request for {Url} had Exception: {Text} after {ms}ms", info.Location, ex.Message, info.Timer.ElapsedMilliseconds);

                return new ProcessResult
                {
                    Location = info.Location,
                    StartTime = startTime,
                    Delay = info.Delay,
                    ElapsedTime = info.Timer.Elapsed,

                };
            }
        }

        public async Task ProcessAsync(Func<ProcessResult, Task> responseHandler, SeleniumPageHandler pageHandler, CancellationToken cancellationToken = default)
        {
            var active = new ConcurrentDictionary<Task<ProcessResult>, ProcessInfo>(_requestOptions.MaxSimultanousRequests, _requestOptions.MaxSimultanousRequests);
            var randomOffset = new Random();
            var requestsHandled = 0;

            while (active.Count > 0 || !_requestQueue.IsEmpty)
            {
                cancellationToken.ThrowIfCancellationRequested();
                while (!_requestQueue.IsEmpty)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (_requestQueue.TryDequeue(out var url))
                    {
                        Double requestDelay = 0;

                        if (_requestOptions.RequestDelay.TotalMilliseconds > 0)
                        {
                            requestDelay = _requestOptions.RequestDelay.TotalMilliseconds;
                            requestDelay += randomOffset.NextDouble() * _requestOptions.RequestDelayOffset.TotalMilliseconds;
                        }

                        var processInfo = new ProcessInfo
                        {
                            Location = url,
                            Timer = new Stopwatch(),
                            Delay = requestDelay,
                            Cancellation = cancellationToken,
                        };

                        Log.Information("Request for {Url} starts with a delay of {ms}ms", url, requestDelay);

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
                    //request handled so need to subtract from pending
                    PendingRequests--;

                    //check if request was ok
                    if (completed.IsFaulted)
                    {
                        var exception = completed.Exception;
                        if (exception != null && exception.InnerException != null)
                        {
                            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                        }
                    }

                    await responseHandler(completed.Result);
                }
            }


            Log.Information("Processed {Count} requests", requestsHandled);
        }

    }
}
