namespace WebCrawler.Models;

//TODO what kind of object for the reason
//text etc?
public enum CompanyCheckReason
{
    RobotRefusal,
    IsNotCompany,
    ParseFailure,
    CompanyFooterCheck,
    CompanyJsonCheck,
    CompanyPagesCheck,
}

public sealed record CompanyCheckResult
(
    bool IsCompany,
    int ConfidenceScore,
    List<CompanyCheckReason> Reasons
);
