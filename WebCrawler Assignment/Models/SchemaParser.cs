using System.Text.Json;

namespace WebCrawler.Models
{
    public class SchemaParser
    {
        public List<StructuredCompanyData> Parse(IEnumerable<string> extractedJson)
        {
            var result = new List<StructuredCompanyData>();

            foreach (var json in extractedJson)
            {
                try
                {
                    using var doc = JsonDocument.Parse(json);

                    ExtractEntities(doc.RootElement, result);
                }
                catch (JsonException)
                {

                }
            }

            return result;
        }

        private void ExtractEntities(JsonElement el, List<StructuredCompanyData> result)
        {
            if (el.ValueKind == JsonValueKind.Array)
            {
                foreach (var e in el.EnumerateArray())
                {
                    ExtractEntities(e, result);
                }

                return;
            }

            if (el.ValueKind != JsonValueKind.Object)
                return;

            //graph
            if (el.TryGetProperty("@graph", out var graph))
            {
                ExtractEntities(graph, result);
            }

            var type = GetString(el, "@type");

            if (IsCompanyType(type) || IsCompanyExclusion(type))
            {
                result.Add(new StructuredCompanyData
                {
                    SchemaType = type,
                    Name = GetString(el, "name"),
                    Url = GetString(el, "url"),
                    Email = GetString(el, "email"),
                    Telephone = GetString(el, "telephone"),

                    StreetAddress = GetNestedString(
                        el, "address", "streetAddress"),

                    PostalCode = GetNestedString(
                        el, "address", "postalCode"),

                    City = GetNestedString(
                        el, "address", "addressLocality"),

                    Country = GetNestedString(
                        el, "address", "addressCountry")
                });
            }

            // Recursively inspect nested objects
            foreach (var property in el.EnumerateObject())
            {
                if (property.Value.ValueKind is
                    JsonValueKind.Object or JsonValueKind.Array)
                {
                    if (property.Name != "@graph")
                    {
                        ExtractEntities(property.Value, result);
                    }
                }
            }
        }

        private static string? GetString(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var property))
                return null;

            return property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : null;
        }

        private static string? GetNestedString(JsonElement element, string parent, string child)
        {
            if (!element.TryGetProperty(parent, out var parentElement))
                return null;

            if (parentElement.ValueKind != JsonValueKind.Object)
                return null;

            return GetString(parentElement, child);
        }

        private static bool IsCompanyType(string? type)
        {
            if (type is null)
                return false;

            var types = type.Split(',');
            return types.Any(type =>
            {
                type = type.Trim().Split('/').Last();
                return StructuredCompanyData.IsCompanyType(type) || StructuredCompanyData.IsMaybeCompanyType(type);
            });
        }

        //these types exclude it being a company
        private static bool IsCompanyExclusion(string? type)
        {
            if (type is null)
                return false;

            var types = type.Split(',');
            return types.Any(type => StructuredCompanyData.IsNotCompanyType(type.Trim().Split('/').Last()));
        }

    }
}
