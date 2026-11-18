namespace WebCrawler.Models;

public sealed record CrawlResult(
    List<string> Html,
    List<string> Text,
    List<string> Css,
    List<string> Javascript,
    List<string> InternalLinks,
    List<string> ExternalLinks
);

public enum CompanyCheckReason
{
    RobotRefusal,
    ParseFailure,
    CancelRequested,
    CompanyFooterIndicationFound,
    NotCompanyFooterIndicationFound,
    CompanyJsonIndicationFound,
    NotCompanyJsonIndicationFound,
    CompanyPagesIndicationFound,
    NotCompanyPagesIndicationFound,
    ConfidenceScoreTooLow,
    ConfidenceScoreMedium,
    ConfidenceScoreHigh,
    ParkingIndicatorFound,
}

public sealed record CompanyCheckResult
(
    bool IsCompany,
    int ConfidenceScore,
    List<string> Reasons
);

public sealed record SessionOpenResult(
    string? Uuid,
    bool Success
);

public sealed record SessionCloseResult(
    bool Success
);