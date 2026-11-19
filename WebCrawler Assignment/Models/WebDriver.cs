using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Edge;
using OpenQA.Selenium.Firefox;
using OpenQA.Selenium.Remote;

namespace WebCrawler.Models;

public enum BrowserType
{
    Chrome,
    Edge,
    Firefox
}

//TODO maybe set a PageLoad timeout (20 seconds or something)
//for the drivers otherwise it can't be cancelled, or change the seleniumPageHandler to GoToUrlAsync ?
//TODO also check if a similar timeout needs to be set for JS execution?

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
                chromeOptions.AddArguments("--headless=new", "--window-size=1920,1080", "--no-sandbox", "--disable-dev-shm-usage", "--disable-blink-features=AutomationControlled");
                driver = new ChromeDriver(chromeOptions);
                break;
            case BrowserType.Edge:
                var edgeOptions = new EdgeOptions();
                edgeOptions.AddArguments("--headless=new", "--window-size=1920,1080", "--no-sandbox", "--disable-dev-shm-usage");
                driver = new EdgeDriver(edgeOptions);
                break;
            case BrowserType.Firefox:
                var firefoxOptions = new FirefoxOptions();
                firefoxOptions.AddArguments("--headless", "--width=1920", "--height=1080", "--disable-dev-shm-usage");
                driver = new FirefoxDriver(firefoxOptions);
                break;
            default:
                throw new ArgumentException($"Unsupported browser type: {browserType}");
        }

        return driver;
    }

    private static ICapabilities GetCapabilities(BrowserType browserType)
    {
        switch (browserType)
        {
            case BrowserType.Chrome:
                ChromeOptions chromeOptions = new();
                chromeOptions.AddArguments("--headless=new", "--window-size=1920,1080", "--no-sandbox", "--disable-dev-shm-usage", "--disable-blink-features=AutomationControlled");
                chromeOptions.AddExcludedArgument("enable-automation");
                chromeOptions.AddAdditionalChromeOption("useAutomationExtension", "False");
                return chromeOptions.ToCapabilities();
            case BrowserType.Edge:
                EdgeOptions edgeOptions = new();
                edgeOptions.AddArguments("--headless=new", "--window-size=1920,1080", "--no-sandbox", "--disable-dev-shm-usage");
                return edgeOptions.ToCapabilities();
            case BrowserType.Firefox:
                FirefoxOptions firefoxOptions = new()
                {
                    PageLoadStrategy = PageLoadStrategy.Normal
                };
                firefoxOptions.AddArguments("--headless", "--width=1920", "--height=1080", "--disable-dev-shm-usage");
                return firefoxOptions.ToCapabilities();
            default:
                throw new ArgumentException($"Unsupported browser type: {browserType}");
        }
    }

    //Using the GridHub
    public static IWebDriver CreateGridInstance(BrowserType browserType, string gridUrl)
    {
        IWebDriver driver = CreateWebDriver(gridUrl, GetCapabilities(browserType));

        return driver;
    }

    private static RemoteWebDriver CreateWebDriver(string hubUrl, ICapabilities capabilities)
    {
        TimeSpan timeSpan = new(0, 3, 0);

        return new RemoteWebDriver(new Uri(hubUrl), capabilities, timeSpan);
    }
}
