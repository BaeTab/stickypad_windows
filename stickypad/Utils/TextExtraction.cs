using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Documents;
using StickyPad.Models;

namespace StickyPad.Utils;

public static class TextExtraction
{
    private const int TitleMaxLength = 80;
    private static readonly Regex TagPattern = new(@"(?<![\w])#([\p{L}\p{N}_\-]{1,32})", RegexOptions.Compiled);
    private static readonly Regex LinkPattern = new(@"\[\[([^\[\]\r\n]{1,200})\]\]", RegexOptions.Compiled);
    private static readonly Regex UrlPattern = new(@"\bhttps?://[^\s\[\]<>""']+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static string ToPlainText(string content, NoteContentFormat format)
    {
        if (string.IsNullOrEmpty(content)) return string.Empty;
        if (format != NoteContentFormat.RichTextXaml) return content;

        try
        {
            var doc = new FlowDocument();
            var range = new TextRange(doc.ContentStart, doc.ContentEnd);
            using var ms = new MemoryStream(Encoding.UTF8.GetBytes(content));
            range.Load(ms, System.Windows.DataFormats.Xaml);
            return range.Text ?? string.Empty;
        }
        catch
        {
            // Fall back to a tag-stripped projection if the XAML is malformed.
            return Regex.Replace(content, "<[^>]+>", string.Empty);
        }
    }

    public static string DeriveTitle(string plainText)
    {
        if (string.IsNullOrWhiteSpace(plainText)) return string.Empty;
        foreach (var rawLine in plainText.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;
            return line.Length <= TitleMaxLength ? line : line[..TitleMaxLength];
        }
        return string.Empty;
    }

    public static List<string> ExtractTags(string plainText)
    {
        if (string.IsNullOrEmpty(plainText)) return new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (Match match in TagPattern.Matches(plainText))
        {
            var tag = match.Groups[1].Value;
            if (seen.Add(tag)) result.Add(tag);
        }
        return result;
    }

    public static IEnumerable<(int Index, int Length, string Title)> FindLinks(string plainText)
    {
        if (string.IsNullOrEmpty(plainText)) yield break;
        foreach (Match match in LinkPattern.Matches(plainText))
        {
            yield return (match.Index, match.Length, match.Groups[1].Value.Trim());
        }
    }

    public static IEnumerable<(int Index, int Length, string Url)> FindUrls(string plainText)
    {
        if (string.IsNullOrEmpty(plainText)) yield break;
        foreach (Match match in UrlPattern.Matches(plainText))
        {
            // Drop trailing punctuation that is almost never part of the URL.
            var url = match.Value.TrimEnd('.', ',', ')', ']', '}', '!', '?', ';', ':', '"', '\'');
            if (url.Length == 0) continue;
            yield return (match.Index, url.Length, url);
        }
    }

    public static string ExcerptOf(string plainText, int max = 160)
    {
        if (string.IsNullOrEmpty(plainText)) return string.Empty;
        var collapsed = Regex.Replace(plainText, @"\s+", " ").Trim();
        return collapsed.Length <= max ? collapsed : collapsed[..max] + "…";
    }
}
