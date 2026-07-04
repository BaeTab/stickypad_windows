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

        switch (format)
        {
            case NoteContentFormat.Markdown:
                // Strip Markdown markup so titles/tags/search index the readable text.
                try { return Markdig.Markdown.ToPlainText(content); }
                catch { return content; }

            case NoteContentFormat.Html:
                return Regex.Replace(content, "<[^>]+>", string.Empty);

            case NoteContentFormat.RichTextXaml:
                // 보안: TextRange.Load 는 비제한 XAML 파서를 써서 ObjectDataProvider 등 가젯이
                // 코드 실행을 일으킬 수 있다. 정상 저장된 FlowDocument XAML 에는 없는 마커가 보이면
                // 파서에 넘기지 않고 태그 제거 텍스트로 대체한다.
                if (ContainsDangerousXaml(content))
                {
                    return Regex.Replace(content, "<[^>]+>", string.Empty);
                }
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

            default:
                return content;
        }
    }

    // 정상 저장된 리치텍스트(FlowDocument) XAML 에는 절대 등장하지 않지만, XAML 코드 실행
    // 가젯(ObjectDataProvider, x:Static 등)이나 임의 타입 매핑(clr-namespace)에는 필요한 마커.
    private static readonly string[] DangerousXamlMarkers =
        { "ObjectDataProvider", "x:Static", "x:Code", "x:FactoryMethod", "clr-namespace" };

    /// 신뢰할 수 없는 XAML 을 비제한 파서(TextRange.Load)에 넘기기 전 걸러내는 보수적 검사.
    /// 하나라도 걸리면 XAML 로 처리하지 않는다. 정상 노트에는 오탐이 없다.
    public static bool ContainsDangerousXaml(string? xaml) =>
        xaml is not null &&
        DangerousXamlMarkers.Any(m => xaml.Contains(m, StringComparison.OrdinalIgnoreCase));

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
