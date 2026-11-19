using OpenQA.Selenium;
using Serilog;
using System.Collections.Concurrent;
using WebCrawler.Configuration;
using WebCrawler.Models;

namespace WebCrawler.Services;

//TODO misschien moet elke sessie ook een timestamp / last used timestamp hebben
//dan kan een andere service de sessie zelf closen na een bepaalde timeout
//dan wordt ie namelijk ook uit de sessions gehaald ook al wordt de /close nooit gecalled
//

public class SeleniumSessionService
{
    private readonly ConcurrentDictionary<Guid, (BrowserType, IWebDriver)> _sessions = [];

    private readonly CrawlerSettings _settings = new();

    private readonly Lock _sessionLock = new();
    private readonly Dictionary<BrowserType, int> _activeSessions = new()
    {
        [BrowserType.Chrome] = 0,
        [BrowserType.Edge] = 0,
        [BrowserType.Firefox] = 0
    };

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
        try
        {
            var driver = WebcrawlerDriver.CreateGridInstance(type, _settings.HubUrl);
            lock (_sessionLock)
            {
                if (!_sessions.TryAdd(uid, (type, driver)))
                {
                    driver.Quit();
                    _activeSessions[type]--;
                    Log.Information("Failed to add the session");
                    return null;
                }
            }
            return uid;
        }
        catch (Exception ex)
        {
            ReleaseSessionSlot(type);
            Log.Warning("Failed to create webdriver for {Type} and url {Url}: {Message}", type, _settings.HubUrl, ex.Message);
            return null;
        }
    }

    public Guid? OpenSessionLocal(BrowserType type)
    {
        Log.Information("Trying to open a session for {Type}", type);
        if (!TryReserveSession(type))
        {
            return null;
        }

        var uid = Guid.NewGuid();
        try
        {
            var driver = WebcrawlerDriver.CreateGridInstance(type);
            lock (_sessionLock)
            {
                if (!_sessions.TryAdd(uid, (type, driver)))
                {
                    driver.Quit();
                    _activeSessions[type]--;
                    Log.Information("Failed to add the session");
                    return null;
                }
            }
            return uid;
        }
        catch (Exception ex)
        {
            ReleaseSessionSlot(type);
            Log.Warning("Failed to create local webdriver for {Type}: {Message}", type, ex.Message);
            return null;
        }
    }

    public bool CloseSession(string uuid)
    {
        Log.Information("Trying to close the session for uuid: {Uid}", uuid);
        if (!Guid.TryParse(uuid, out var uid))
        {
            Log.Information("Failed to parse uuid {Uid}", uuid);
            return false;
        }
        (BrowserType Type, IWebDriver Driver) session;

        lock (_sessionLock)
        {
            if (!_sessions.Remove(uid, out session))
            {
                Log.Information("Failed to close session for uid {Uid} session not found", uuid);
                return false;
            }

            _activeSessions[session.Type]--;
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

    public IWebDriver? GetSession(string uuid)
    {
        Log.Information("Trying to get the session for uuid: {Uid}", uuid);
        if (Guid.TryParse(uuid, out var uid))
        {
            if (_sessions.TryGetValue(uid, out var driver))
            {
                return driver.Item2;
            }
        }
        Log.Warning("Failed to find session for uuid: {Uid}", uuid);
        return null;
    }
}
