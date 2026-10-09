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
        IWebDriver driver = null;

        switch (browserType)
        {
            case BrowserType.Chrome:
                var chromeOptions = new ChromeOptions();
                chromeOptions.AddArguments("--headless=new");
                driver = new ChromeDriver(chromeOptions);
                Log.Information("creating local webdriver for Chrome.");
                break;
            case BrowserType.Edge:
                var edgeOptions = new EdgeOptions();
                edgeOptions.AddArguments("--headless=new");
                driver = new EdgeDriver(edgeOptions);
                Log.Information("creating local webdriver for Edge.");
                break;
            case BrowserType.Firefox:
                var firefoxOptions = new FirefoxOptions();
                firefoxOptions.AddArguments("--headless=new");
                driver = new FirefoxDriver(firefoxOptions);
                Log.Information("creating local webdriver for Firefox.");
                break;
        }

        return driver;
    }

    //Using the GridHub
    public static IWebDriver CreateGridInstance(BrowserType browserType, string gridUrl)
    {
        IWebDriver driver = null;
        TimeSpan timeSpan = new TimeSpan(0, 3, 0);

        switch (browserType)
        {
            case BrowserType.Chrome:
                ChromeOptions chromeOptions = new ChromeOptions();
                chromeOptions.AddArguments("--headless=new");
                driver = CreateWebDriver(gridUrl, chromeOptions.ToCapabilities());
                Log.Information("creating hub webdriver for Chrome to url {Url}.", gridUrl);
                break;
            case BrowserType.Edge:
                EdgeOptions edgeOptions = new EdgeOptions();
                edgeOptions.AddArguments("--headless=new");
                driver = CreateWebDriver(gridUrl, edgeOptions.ToCapabilities());
                Log.Information("creating hub webdriver for Edge to url {Url}.", gridUrl);
                break;
            case BrowserType.Firefox:
                FirefoxOptions firefoxOptions = new FirefoxOptions();
                firefoxOptions.AddArguments("--headless=new");
                driver = CreateWebDriver(gridUrl, firefoxOptions.ToCapabilities());
                Log.Information("creating hub webdriver for Firefox to url {Url}.", gridUrl);
                break;
        }

        return driver;
    }

    private static IWebDriver CreateWebDriver(string hubUrl, ICapabilities capabilities)
    {
        TimeSpan timeSpan = new TimeSpan(0, 3, 0);
        return new RemoteWebDriver(
                    new Uri(hubUrl),
                    capabilities,
                    timeSpan
                );
    }
}
