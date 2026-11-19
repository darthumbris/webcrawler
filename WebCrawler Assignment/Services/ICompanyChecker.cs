using WebCrawler.Models;

namespace WebCrawler.Services;

public interface ICompanyChecker
{
    Task<CompanyCheckResult> CheckCompanyAsync(string domainUrl, SessionEntry session, CancellationToken cancellationToken);
}
