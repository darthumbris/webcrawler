using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Edge;
using OpenQA.Selenium.Firefox;
using OpenQA.Selenium.Remote;
using Serilog;

namespace WebCrawler.Models;

public enum BrowserType
{
    Chrome,
    Edge,
    Firefox
}

public class WebcrawlerDriver
{
    //Local
    public static IWebDriver CreateGridInstance(BrowserType browserType)
    {
        IWebDriver driver;

        switch (browserType)
        {
            case BrowserType.Chrome:
                var chromeOptions = new ChromeOptions();
                chromeOptions.AddArguments("--headless=new", "--window-size=1920,1080", "--no-sandbox", "--disable-dev-shm-usage");
                driver = new ChromeDriver(chromeOptions);
                Log.Information("creating local webdriver for Chrome.");
                break;
            case BrowserType.Edge:
                var edgeOptions = new EdgeOptions();
                edgeOptions.AddArguments("--headless=new", "--window-size=1920,1080", "--no-sandbox", "--disable-dev-shm-usage");
                driver = new EdgeDriver(edgeOptions);
                Log.Information("creating local webdriver for Edge.");
                break;
            case BrowserType.Firefox:
                var firefoxOptions = new FirefoxOptions();
                firefoxOptions.AddArguments("--headless", "--width=1920", "--height=1080", "--disable-dev-shm-usage");
                driver = new FirefoxDriver(firefoxOptions);
                Log.Information("creating local webdriver for Firefox.");
                break;
            default:
                throw new ArgumentException($"Unsupported browser type: {browserType}");
        }

        return driver;
    }

    //Using the GridHub
    public static IWebDriver CreateGridInstance(BrowserType browserType, string gridUrl)
    {
        IWebDriver driver;

        switch (browserType)
        {
            case BrowserType.Chrome:
                ChromeOptions chromeOptions = new();
                chromeOptions.AddArguments("--headless=new", "--window-size=1920,1080", "--no-sandbox", "--disable-dev-shm-usage");
                driver = CreateWebDriver(gridUrl, chromeOptions.ToCapabilities());
                Log.Information("creating hub webdriver for Chrome to url {Url}.", gridUrl);
                break;
            case BrowserType.Edge:
                EdgeOptions edgeOptions = new();
                edgeOptions.AddArguments("--headless=new", "--window-size=1920,1080", "--no-sandbox", "--disable-dev-shm-usage");
                driver = CreateWebDriver(gridUrl, edgeOptions.ToCapabilities());
                Log.Information("creating hub webdriver for Edge to url {Url}.", gridUrl);
                break;
            case BrowserType.Firefox:
                FirefoxOptions firefoxOptions = new();
                firefoxOptions.PageLoadStrategy = PageLoadStrategy.Normal;
                Log.Information("pageloadstrategy: {Strategy}", firefoxOptions.PageLoadStrategy);
                firefoxOptions.AddArguments("--headless", "--width=1920", "--height=1080", "--disable-dev-shm-usage");
                driver = CreateWebDriver(gridUrl, firefoxOptions.ToCapabilities());
                Log.Information("creating hub webdriver for Firefox to url {Url}.", gridUrl);
                break;
            default:
                throw new ArgumentException($"Unsupported browser type: {browserType}");
        }

        return driver;
    }

    private static RemoteWebDriver CreateWebDriver(string hubUrl, ICapabilities capabilities)
    {
        TimeSpan timeSpan = new(0, 3, 0);
        return new RemoteWebDriver(new Uri(hubUrl), capabilities, timeSpan);
    }
}
