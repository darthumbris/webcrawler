using OpenQA.Selenium;
using Serilog;
using System.Collections.Concurrent;
using WebCrawler.Configuration;
using WebCrawler.Models;

namespace WebCrawler.Services;

public class SeleniumSessionService
{
    private readonly ConcurrentDictionary<Guid, (BrowserType, IWebDriver)> _sessions = [];

    private readonly CrawlerSettings _settings = new();

    private bool CheckLimit(BrowserType type)
    {
        switch (type)
        {
            case BrowserType.Chrome:
                if (_sessions.Values.Select(s => s.Item1 == BrowserType.Chrome).Count() >= _settings.MaxChromeSessions)
                {
                    Log.Information("Reached session limit {Limit} for Chrome", _settings.MaxChromeSessions);
                    return false;
                }
                break;
            case BrowserType.Edge:
                if (_sessions.Values.Select(s => s.Item1 == BrowserType.Edge).Count() >= _settings.MaxEdgeSessions)
                {
                    Log.Information("Reached session limit {Limit} for Edge", _settings.MaxEdgeSessions);
                    return false;
                }
                break;
            case BrowserType.Firefox:
                if (_sessions.Values.Select(s => s.Item1 == BrowserType.Firefox).Count() >= _settings.MaxFirefoxSessions)
                {
                    Log.Information("Reached session limit {Limit} for Firefox", _settings.MaxFirefoxSessions);
                    return false;
                }
                break;
            default:
                throw new ArgumentException($"Unsupported browser type: {type}");
        }
        return true;
    }

    public Guid? OpenSessionRemote(BrowserType type)
    {
        Log.Information("Trying to open a session for {Type}", type);
        if (!CheckLimit(type))
        {
            return null;
        }
        var uid = Guid.NewGuid();
        try
        {
            var driver = WebcrawlerDriver.CreateGridInstance(type, _settings.HubUrl);
            //TODO check the TryAdd probably
            _sessions.TryAdd(uid, (type, driver));
        }
        catch (Exception ex)
        {
            Log.Warning("Failed to create webdriver for {Type} and url {Url}: {Message}", type, _settings.HubUrl, ex.Message);
            return null;
        }
        return uid;
    }

    public Guid? OpenSessionLocal(BrowserType type)
    {
        Log.Information("Trying to open a session for {Type}", type);
        if (!CheckLimit(type))
        {
            return null;
        }
        var uid = Guid.NewGuid();
        try
        {
            var driver = WebcrawlerDriver.CreateGridInstance(type);
            //TODO check the TryAdd probably
            _sessions.TryAdd(uid, (type, driver));
        }
        catch (Exception ex)
        {
            Log.Warning("Failed to create webdriver for {Type} and: {Message}", type, ex.Message);
            return null;
        }
        return uid;
    }

    public bool CloseSession(string uuid)
    {
        Log.Information("Trying to close the session for uuid: {Uid}", uuid);
        var uid = new Guid(uuid);
        if (_sessions.TryGetValue(uid, out var driver))
        {
            driver.Item2.Quit();
            return true;
        }

        Log.Information("Failed to close session for uid {Uid} session not found", uid);
        return false;
    }

    public IWebDriver? GetSession(string uuid)
    {
        Log.Information("Trying to get the session for uuid: {Uid}", uuid);
        var uid = new Guid(uuid);
        if (_sessions.TryGetValue(uid, out var driver))
        {
            return driver.Item2;
        }
        Log.Warning("Failed to find session for uuid: {Uid}", uuid);
        return null;
    }
}
