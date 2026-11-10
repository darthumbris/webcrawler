namespace WebCrawler.Models;

//TODO what reasons to add and how to word properly?
public enum CompanyCheckReason
{
    RobotRefusal,
    ConfidenceScoreTooLow,
    ParseFailure,
    CompanyFooterCheck,
    CompanyJsonCheck,
    CompanyPagesCheck,
    CompanyScoreThreshold,
    IsNotCompanyIndicatorFound,
    ParkingIndicator,
}

public sealed record CompanyCheckResult
(
    bool IsCompany,
    int ConfidenceScore,
    List<string> Reasons
);
