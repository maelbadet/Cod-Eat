using System.Net;
using System.Text.RegularExpressions;

namespace CodEat.Web;

// Convert external HTML to inert text while retaining the structure needed by the importer.
// This is not an HTML renderer: no imported tags, scripts, styles or link targets are displayed.
public static class BookStackContent
{
    static readonly TimeSpan Limit = TimeSpan.FromSeconds(1);
    const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant;

    static string Replace(string value, string pattern, string replacement)
        => Regex.Replace(value, pattern, replacement, Options, Limit);

    public static string Inline(string value)
    {
        value = Replace(value, @"<br\s*/?>", " ");
        value = Replace(value, @"<[^>]*>", "");
        value = WebUtility.HtmlDecode(value);
        value = Replace(value, @"\[([^\]]+)\]\([^)]*\)", "$1");
        value = value.Replace("**", "").Replace("__", "").Replace('`', ' ');
        return Replace(value, @"\s+", " ").Trim();
    }

    public static string FromHtml(string html)
    {
        html = Replace(html, @"<(script|style|noscript)\b[^>]*>.*?</\1\s*>", "");
        html = Replace(html, @"<!--.*?-->", "");
        html = Regex.Replace(html, @"<table\b[^>]*>(.*?)</table\s*>", table =>
        {
            var rows = Regex.Matches(table.Groups[1].Value, @"<tr\b[^>]*>(.*?)</tr\s*>", Options, Limit)
                .Select(row => Regex.Matches(row.Groups[1].Value, @"<t[hd]\b[^>]*>(.*?)</t[hd]\s*>", Options, Limit)
                    .Select(cell => Inline(cell.Groups[1].Value).Replace("|", "\\|")).ToArray())
                .Where(row => row.Length > 0).ToArray();
            if (rows.Length == 0) return "\n" + Inline(table.Value) + "\n";
            var result = new List<string> { "", "| " + string.Join(" | ", rows[0]) + " |",
                "| " + string.Join(" | ", rows[0].Select(_ => "---")) + " |" };
            result.AddRange(rows.Skip(1).Select(row => "| " + string.Join(" | ", row) + " |"));
            result.Add("");
            return string.Join('\n', result);
        }, Options, Limit);
        html = Regex.Replace(html, @"<h([1-6])\b[^>]*>(.*?)</h\1\s*>", heading =>
            "\n\n" + new string('#', int.Parse(heading.Groups[1].Value)) + " " + Inline(heading.Groups[2].Value) + "\n\n", Options, Limit);
        html = Regex.Replace(html, @"<ol\b[^>]*>(.*?)</ol\s*>", list =>
        {
            var number = 0;
            return "\n" + Regex.Replace(list.Groups[1].Value, @"<li\b[^>]*>(.*?)</li\s*>", item =>
                $"\n{++number}. {Inline(item.Groups[1].Value)}\n", Options, Limit) + "\n";
        }, Options, Limit);
        html = Replace(html, @"<li\b[^>]*>", "\n- ");
        html = Replace(html, @"</(?:li|p|div|section|blockquote|ul|ol)\s*>", "\n\n");
        html = Replace(html, @"<br\s*/?>", "\n");
        html = Replace(html, @"<[^>]*>", "");
        html = WebUtility.HtmlDecode(html).Replace("\r\n", "\n").Replace('\r', '\n');
        html = Replace(html, @"[\t ]+\n", "\n");
        return Replace(html, @"\n{3,}", "\n\n").Trim();
    }
}
