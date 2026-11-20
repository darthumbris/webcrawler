using OpenQA.Selenium;
using Serilog;
using WebCrawler.Configuration;
using WebCrawler.Models;
using Microsoft.Extensions.Options;

namespace WebCrawler.Services;

public sealed record SessionOpenResponse(Guid? Id, SessionResponse Response);
public sealed record RequestResponse(SessionEntry? Session, SessionResponse Response);

public enum SessionResponse
{
    SessionNotFound,
    SessionFound,
    SessionClosedSuccessfully,
    SessionLimitReached,
    SessionCreationFailure,
    SessionCreatedSuccessfully,
    GuidParseFailure,
    SessionRequestOngoing,
    SessionCloseFailure,
    UnsupportedBrowserType,
}

public class SeleniumSessionService(IOptions<CrawlerSettings> options) : IDisposable
{
    private readonly Dictionary<Guid, SessionEntry> _sessions = [];

    private readonly CrawlerSettings _settings = options.Value;

    private readonly Lock _sessionLock = new();
    private readonly Dictionary<BrowserType, int> _activeSessions = new()
    {
        [BrowserType.Chrome] = 0,
        [BrowserType.Edge] = 0,
        [BrowserType.Firefox] = 0
    };

    public void CloseExpiredSessions()
    {
        List<(Guid Id, SessionEntry Session)> expiredSessions = [];

        lock (_sessionLock)
        {
            var cutoff = DateTimeOffset.UtcNow - SessionTimeout;

            foreach (var sessionPair in _sessions)
            {
                var session = sessionPair.Value;

                if (session.LastActivity <= cutoff && !session.RequestOnGoing && _sessions.Remove(sessionPair.Key, out var removedSession))
                {
                    _activeSessions[removedSession.BrowserType]--;
                    expiredSessions.Add((sessionPair.Key, removedSession));
                }
            }
        }

        foreach (var (Id, Session) in expiredSessions)
        {
            try
            {
                Session.Driver.Quit();
                Log.Information("Closed expired Selenium session {Id}", Id);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to close expired Selenium session {Id}", Id);
            }
        }
    }

    private static readonly TimeSpan SessionTimeout = TimeSpan.FromMinutes(15);

    private int GetMaximumSessions(BrowserType type)
    {
        return type switch
        {
            BrowserType.Chrome => _settings.MaxChromeSessions,
            BrowserType.Edge => _settings.MaxEdgeSessions,
            BrowserType.Firefox => _settings.MaxFirefoxSessions,
            _ => throw new ArgumentException(
                $"Unsupported browser type: {type}",
                nameof(type))
        };
    }

    private bool TryReserveSession(BrowserType type)
    {
        lock (_sessionLock)
        {
            var maximumSessions = GetMaximumSessions(type);
            var activeSessions = _activeSessions[type];

            if (activeSessions >= maximumSessions)
            {
                Log.Information("Reached session limit {Limit} for {Type}", maximumSessions, type);
                return false;
            }

            _activeSessions[type]++;
            return true;
        }
    }

    private void ReleaseSessionSlot(BrowserType type)
    {
        lock (_sessionLock)
        {
            _activeSessions[type]--;
        }
    }

    private static void DisposeDriver(IWebDriver? driver)
    {
        if (driver is null)
        {
            return;
        }

        try
        {
            driver.Quit();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to quit selenium driver.");
        }
    }

    private SessionOpenResponse OpenSession(BrowserType type, Func<IWebDriver> createDriver)
    {
        Log.Information("Trying to open a session for {Type}", type);
        if (!Enum.IsDefined(type))
        {
            return new SessionOpenResponse(null, SessionResponse.UnsupportedBrowserType);
        }
        if (!TryReserveSession(type))
        {
            return new SessionOpenResponse(null, SessionResponse.SessionLimitReached);
        }

        var uid = Guid.NewGuid();
        IWebDriver? driver = null;
        try
        {
            driver = createDriver();
            driver.Manage().Timeouts().PageLoad = _settings.PageLoadTimeOut;

            lock (_sessionLock)
            {
                if (_sessions.TryAdd(uid, new SessionEntry(type, driver)))
                {
                    return new SessionOpenResponse(uid, SessionResponse.SessionCreatedSuccessfully);
                }
            }
            Log.Warning("Failed to add session {Id}", uid);
            DisposeDriver(driver);
            ReleaseSessionSlot(type);
            return new SessionOpenResponse(null, SessionResponse.SessionCreationFailure);
        }
        catch (Exception ex)
        {
            DisposeDriver(driver);
            ReleaseSessionSlot(type);

            Log.Warning(ex, "Failed to create webdriver for {Type}", type);
            return new SessionOpenResponse(null, SessionResponse.SessionCreationFailure);
        }
    }

    public SessionOpenResponse OpenSessionRemote(BrowserType type)
    {
        return OpenSession(type, () => WebcrawlerDriver.CreateGridInstance(type, _settings.HubUrl));
    }

    public SessionOpenResponse OpenSessionLocal(BrowserType type)
    {
        return OpenSession(type, () => WebcrawlerDriver.CreateGridInstance(type));
    }

    public SessionResponse CloseSession(string uuid)
    {
        Log.Information("Trying to close the session for uuid: {Uid}", uuid);
        if (!Guid.TryParse(uuid, out var uid))
        {
            Log.Information("Failed to parse uuid {Uid}", uuid);
            return SessionResponse.GuidParseFailure;
        }
        SessionEntry? session;

        lock (_sessionLock)
        {
            if (!_sessions.TryGetValue(uid, out session))
            {
                Log.Information("Failed to close session for uid {Uid} session not found", uuid);
                return SessionResponse.SessionNotFound;
            }

            if (session.RequestOnGoing)
            {
                Log.Information("Can't close session {Id} because a request is ongoing.", uuid);
                return SessionResponse.SessionRequestOngoing;
            }

            _sessions.Remove(uid, out _);
            _activeSessions[session.BrowserType]--;
        }

        try
        {
            session.Driver.Quit();
            return SessionResponse.SessionClosedSuccessfully;
        }
        catch (Exception ex)
        {
            Log.Warning("Failed to close session {Uid} with exception {Exception}", uuid, ex.Message);
            return SessionResponse.SessionCloseFailure;
        }
    }

    public RequestResponse BeginRequest(string uuid)
    {
        Log.Information("Trying to get the session for uuid: {Uid}", uuid);
        if (!Guid.TryParse(uuid, out var uid))
        {
            return new RequestResponse(null, SessionResponse.GuidParseFailure);
        }
        lock (_sessionLock)
        {
            if (!_sessions.TryGetValue(uid, out var session))
            {
                return new RequestResponse(null, SessionResponse.SessionNotFound);
            }

            if (session.RequestOnGoing)
            {
                Log.Warning("There is already a request ongoing. Can't start multiple requests for a single session.");
                return new RequestResponse(null, SessionResponse.SessionRequestOngoing);
            }

            session.RequestOnGoing = true;
            session.LastActivity = DateTimeOffset.UtcNow;

            return new RequestResponse(session, SessionResponse.SessionFound);
        }
    }

    public void EndRequest(string uuid)
    {
        if (!Guid.TryParse(uuid, out var uid)) //should not happen since request was started with same uuid
        {
            return;
        }
        lock (_sessionLock)
        {
            if (_sessions.TryGetValue(uid, out var session))
            {
                session.RequestOnGoing = false;
                session.LastActivity = DateTimeOffset.UtcNow;
            }
        }
    }

    public void Dispose()
    {
        List<SessionEntry> sessionsToClose;

        lock (_sessionLock)
        {
            sessionsToClose = [.. _sessions.Values];

            _sessions.Clear();

            foreach (var browserType in _activeSessions.Keys)
            {
                _activeSessions[browserType] = 0;
            }
        }

        foreach (var session in sessionsToClose)
        {
            DisposeDriver(session.Driver);
        }

        GC.SuppressFinalize(this);
    }
}
