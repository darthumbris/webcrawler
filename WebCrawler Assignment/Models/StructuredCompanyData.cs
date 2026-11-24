namespace WebCrawler.Models;

public class StructuredCompanyData
{
    public string? Name { get; set; }

    public string? Url { get; set; }

    public string? Email { get; set; }

    public string? Telephone { get; set; }

    public IReadOnlyCollection<string> SchemaType { get; set; } = [];

    public string? StreetAddress { get; set; }

    public string? PostalCode { get; set; }

    public string? City { get; set; }

    public string? Country { get; set; }

    public static bool IsCompanyType(string? type)
    {
        return type != null && (
            type.Equals("Corporation", StringComparison.InvariantCultureIgnoreCase) ||
            type.Equals("LocalBusiness", StringComparison.InvariantCultureIgnoreCase) ||
            type.Equals("Store", StringComparison.InvariantCultureIgnoreCase) ||
            type.Equals("ProfessionalService", StringComparison.InvariantCultureIgnoreCase) ||
            type.Equals("Restaurant", StringComparison.InvariantCultureIgnoreCase) ||
            type.Equals("Consortium", StringComparison.InvariantCultureIgnoreCase) ||
            type.Equals("OnlineBusiness", StringComparison.InvariantCultureIgnoreCase)
            );
    }

    public static bool IsMaybeCompanyType(string? type)
    {
        return type != null && (type.Equals("Organization", StringComparison.InvariantCultureIgnoreCase));
    }

    public static bool IsNotCompanyType(string? type)
    {
        return type != null && (
            type.Equals("Person", StringComparison.InvariantCultureIgnoreCase) ||
            type.Equals("Blog", StringComparison.InvariantCultureIgnoreCase) ||
            type.Equals("BlogPosting", StringComparison.InvariantCultureIgnoreCase) ||
            type.Equals("EducationalOrganization", StringComparison.InvariantCultureIgnoreCase) ||
            type.Equals("GovernmentOrganization", StringComparison.InvariantCultureIgnoreCase) ||
            type.Equals("NGO", StringComparison.InvariantCultureIgnoreCase)
            );
    }
}
