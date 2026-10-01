namespace WebCrawler.Models;

internal enum LineType
{
    UserAgent,
    Rule,
    DelayRule,
    Comment,
    Sitemap,
    Unknown,
}

internal class RobotsLine
{
    public LineType Type {  get;}
    public string Raw { get; }
    public string? Field { get; }
    public string? Value { get; }

    public RobotsLine(string line) {
        if (string.IsNullOrWhiteSpace(line))
        {
            throw new ArgumentException("Can't create a line from null", nameof(line));
        }
        Raw = line;

        var trim = line.Trim();

        //comment
        if (trim.StartsWith("#", StringComparison.Ordinal))
        {
            Type = LineType.Comment;
            //return because comment does not have value etc
            return;
        }
        //Inline comment removal
        var commentIndex = trim.IndexOf('#');
        if (commentIndex >= 0)
        {
            trim = trim.Substring(0, commentIndex).TrimEnd();
        }
        
        //field
        var colonIndex = trim.IndexOf(':');
        var field = colonIndex < 0 ? string.Empty : trim.Substring(0, colonIndex);

        Field = field.Trim();

        //type
        Type = GetLineType(Field);
        if (Type == LineType.Unknown)
        {
            return;
        }

        //value
        var seperatorIndex = field.Length;
        Value = seperatorIndex + 1 < trim.Length ? trim.Substring(seperatorIndex + 1).Trim() : string.Empty;
    }

    static LineType GetLineType(string field)
    {
        if (string.IsNullOrWhiteSpace(field))
        {
            return LineType.Unknown;
        }

        return field.ToLowerInvariant() switch
        {
            "user-agent" => LineType.UserAgent,
            "allow" => LineType.Rule,
            "disallow" => LineType.Rule,
            "crawl-delay" => LineType.DelayRule,
            "sitemap" => LineType.Sitemap,
            _ => LineType.Unknown,
        };
    }
}
