namespace WebCrawler.Models;

public enum CompanyCheckReason
{
    RobotRefusal,
    ParseFailure,
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
