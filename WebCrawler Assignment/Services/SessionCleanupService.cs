namespace WebCrawler.Services;

//TODO does the cleanup service need to be stopped by the cancellationToken????
public sealed class SessionCleanupService(SeleniumSessionService sessions, ILogger<SessionCleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                sessions.CloseExpiredSessions();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation("Session cleanup service stopped.");
        }
    }
}
