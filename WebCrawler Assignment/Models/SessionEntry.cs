using OpenQA.Selenium;

namespace WebCrawler.Models;

public sealed class SessionEntry(BrowserType browserType, IWebDriver driver)
{
    public BrowserType BrowserType { get; } = browserType;
    public IWebDriver Driver { get; } = driver;

    public DateTimeOffset CreatedTime { get; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastActivity { get; internal set; } = DateTimeOffset.UtcNow;

    public bool RequestOnGoing { get; internal set; }

    public bool DriverInvalidated { get; internal set; }
}
