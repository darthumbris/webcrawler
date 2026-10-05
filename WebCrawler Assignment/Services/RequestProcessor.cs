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

        private async Task<ProcessResult> ProcessAsync(ProcessInfo info, SeleniumPageFetcher pageFetcher)
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

                var fetchedPage = await pageFetcher.FetchPageAsync(info.Location.ToString(), cancel);

                info.Timer.Stop();

                info.Cancellation.ThrowIfCancellationRequested();

                Log.Information("Request completed in {ms}ms", info.Timer.ElapsedMilliseconds);

                return new ProcessResult
                {
                    Location = info.Location,
                    StartTime = startTime,
                    Delay = info.Delay,
                    Content = fetchedPage,
                    ElapsedTime = info.Timer.Elapsed,
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

        public async Task ProcessAsync(Func<ProcessResult, Task> responseHandler, SeleniumPageFetcher pageFetcher, CancellationToken cancellationToken = default)
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

                        Log.Information("Request for {Url} starts with a delay of {ms}", url, requestDelay);

                        var task = ProcessAsync(processInfo, pageFetcher);
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
