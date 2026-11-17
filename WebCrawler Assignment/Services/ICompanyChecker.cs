using OpenQA.Selenium;
using WebCrawler.Models;

namespace WebCrawler.Services;

public interface ICompanyChecker
{
    Task<CompanyCheckResult> CheckCompanyAsync(string domainUrl, IWebDriver driver, CancellationToken cancellationToken);
}
