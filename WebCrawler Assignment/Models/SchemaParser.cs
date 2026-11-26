using Serilog;
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
                catch (JsonException ex)
                {
                    Log.Information(ex, "Ignoring invalid JSON-LD data.");
                }
            }

            return result;
        }

        private static void ExtractEntities(JsonElement el, List<StructuredCompanyData> result)
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

            var types = NormalizeTypes(GetTypes(el));

            if (types.Any(StructuredCompanyData.IsCompanyType) ||
                types.Any(StructuredCompanyData.IsMaybeCompanyType) ||
                types.Any(StructuredCompanyData.IsNotCompanyType))
            {
                result.Add(new StructuredCompanyData
                {
                    SchemaType = [.. types],
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

        private static IEnumerable<string> NormalizeTypes(IEnumerable<string> types)
        {
            return types.SelectMany(type => type.Split(',', StringSplitOptions.RemoveEmptyEntries))
                .Select(type => type.Trim()).Where(type => !string.IsNullOrEmpty(type))
                .Select(type => type.Split('/').Last());
        }

        private static IEnumerable<string> GetTypes(JsonElement element)
        {
            if (!element.TryGetProperty("@type", out var type))
            {
                yield break;
            }

            if (type.ValueKind == JsonValueKind.String)
            {
                var value = type.GetString();

                if (!string.IsNullOrWhiteSpace(value))
                {
                    yield return value;
                }

                yield break;
            }

            if (type.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in type.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        var value = item.GetString();

                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            yield return value;
                        }
                    }
                }
            }
        }

        private static string? GetNestedString(JsonElement element, string parent, string child)
        {
            if (!element.TryGetProperty(parent, out var parentElement))
                return null;

            if (parentElement.ValueKind != JsonValueKind.Object)
                return null;

            return GetString(parentElement, child);
        }
    }
}
