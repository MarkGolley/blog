using System.Text.Encodings.Web;
using System.Text.RegularExpressions;

namespace MyBlog.Services;

public static class CommentContentFormatter
{
    private static readonly Regex CodeRegex = new(@"`([^`\r\n]+)`", RegexOptions.Compiled);
    private static readonly Regex BoldRegex = new(@"\*\*(.+?)\*\*", RegexOptions.Compiled);
    private static readonly Regex ItalicRegex = new(@"\*(.+?)\*", RegexOptions.Compiled);
    private static readonly Regex LinkRegex = new(@"\[([^\]]+)\]\(([^)]+)\)", RegexOptions.Compiled);

    public static string ToSafeHtml(string? rawContent)
    {
        if (string.IsNullOrWhiteSpace(rawContent))
        {
            return string.Empty;
        }

        var encoded = HtmlEncoder.Default.Encode(rawContent.Trim());
        var withCode = CodeRegex.Replace(encoded, "<code>$1</code>");
        var withBold = BoldRegex.Replace(withCode, "<strong>$1</strong>");
        var withItalic = ItalicRegex.Replace(withBold, "<em>$1</em>");
        var withLinks = LinkRegex.Replace(withItalic, BuildSafeLink);

        return withLinks.Replace("\r\n", "<br />").Replace("\n", "<br />");
    }

    private static string BuildSafeLink(Match match)
    {
        var label = match.Groups[1].Value;
        var url = match.Groups[2].Value;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return $"{label} ({url})";
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return $"{label} ({url})";
        }

        return $"<a href=\"{uri.AbsoluteUri}\" target=\"_blank\" rel=\"noopener noreferrer\">{label}</a>";
    }
}
