namespace WebCrawler.Models;

//TODO what reasons to add and how to word properly?
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
