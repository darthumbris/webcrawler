using OpenQA.Selenium;
using Serilog;
using System.Collections.Concurrent;
using WebCrawler.Configuration;
using WebCrawler.Models;

namespace WebCrawler.Services;

public class SeleniumSessionService
{
    private readonly ConcurrentDictionary<Guid, SessionEntry> _sessions = [];

    private readonly CrawlerSettings _settings = new();

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

                if (session.LastActivity <= cutoff && !session.RequestOnGoing && _sessions.TryRemove(sessionPair.Key, out var removedSession))
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

    public Guid? OpenSessionRemote(BrowserType type)
    {
        Log.Information("Trying to open a session for {Type}", type);
        if (!TryReserveSession(type))
        {
            return null;
        }

        var uid = Guid.NewGuid();
        IWebDriver driver;
        try
        {
            driver = WebcrawlerDriver.CreateGridInstance(type, _settings.HubUrl);
        }
        catch (Exception ex)
        {
            ReleaseSessionSlot(type);
            Log.Information("Failed to create webdriver for {Type} and url {Url}: {Message}", type, _settings.HubUrl, ex.Message);
            return null;
        }
        bool added;

        lock (_sessionLock)
        {
            added = _sessions.TryAdd(
                uid,
                new SessionEntry(type, driver));
        }
        if (!added)
        {
            try
            {
                driver.Quit();
            }
            finally
            {
                ReleaseSessionSlot(type);
            }

            Log.Warning("Failed to add session {Id}", uid);
            return null;
        }
        return uid;
    }

    public Guid? OpenSessionLocal(BrowserType type)
    {
        Log.Information("Trying to open a session for {Type}", type);
        if (!TryReserveSession(type))
        {
            return null;
        }

        var uid = Guid.NewGuid();
        IWebDriver driver;
        try
        {
            driver = WebcrawlerDriver.CreateGridInstance(type);
        }
        catch (Exception ex)
        {
            ReleaseSessionSlot(type);
            Log.Information("Failed to create local webdriver for {Type}: {Message}", type, ex.Message);
            return null;
        }
        bool added;

        lock (_sessionLock)
        {
            added = _sessions.TryAdd(
                uid,
                new SessionEntry(type, driver));
        }
        if (!added)
        {
            try
            {
                driver.Quit();
            }
            finally
            {
                ReleaseSessionSlot(type);
            }

            Log.Warning("Failed to add session {Id}", uid);
            return null;
        }
        return uid;
    }

    public bool CloseSession(string uuid)
    {
        Log.Information("Trying to close the session for uuid: {Uid}", uuid);
        if (!Guid.TryParse(uuid, out var uid))
        {
            Log.Information("Failed to parse uuid {Uid}", uuid);
            return false;
        }
        SessionEntry? session;

        lock (_sessionLock)
        {
            if (!_sessions.TryGetValue(uid, out session))
            {
                Log.Information("Failed to close session for uid {Uid} session not found", uuid);
                return false;
            }

            if (session.RequestOnGoing)
            {
                Log.Information("Can't close session {Id} because a request is ongoing.", uuid);
                return false;
            }

            _sessions.TryRemove(uid, out _);
            _activeSessions[session.BrowserType]--;
        }

        try
        {
            session.Driver.Quit();
            return true;
        }
        catch (Exception ex)
        {
            //If the session fails to quit the Hub itself should quit it after a timeout
            //or that maybe why it fails to quit because the session doesn't exist anymore
            Log.Warning("Failed to close session {Uid} with exception {Exception}", uuid, ex.Message);
            return false;
        }
    }

    public SessionEntry? BeginRequest(string uuid)
    {
        Log.Information("Trying to get the session for uuid: {Uid}", uuid);
        if (!Guid.TryParse(uuid, out var uid))
        {
            return null;
        }
        lock (_sessionLock)
        {
            if (!_sessions.TryGetValue(uid, out var session))
            {
                return null;
            }

            if (session.RequestOnGoing)
            {
                Log.Warning("There is already a request ongoing. Can't start multiple requests for a single session.");
                return null;
            }

            session.RequestOnGoing = true;
            session.LastActivity = DateTimeOffset.UtcNow;

            return session;
        }
    }

    public void EndRequest(string uuid)
    {
        Log.Information("Trying to close the session for uuid: {Uid}", uuid);
        if (!Guid.TryParse(uuid, out var uid))
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
}
