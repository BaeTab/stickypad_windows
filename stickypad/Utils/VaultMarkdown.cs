using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using StickyPad.Models;

namespace StickyPad.Utils;

/// 노트를 볼트(폴더)의 단일 `.md` 파일과 왕복 변환한다 — 2.0 볼트 모드의 핵심 primitive.
/// 사람이 읽는 본문 + YAML 프런트매터에 왕복에 필요한 메타(id·title·format·color·tags·날짜)를 담는다.
///
/// 규칙:
/// - Markdown/HTML/PlainText 노트는 본문이 원본 소스 그대로라 무손실 왕복된다.
/// - RichTextXaml 노트는 볼트를 사람이 읽을 수 있게 유지하려고 PlainText 로 내려서 저장한다(서식 손실).
/// - 프런트매터가 없는 손수 만든 `.md` 도 관대하게 읽어들인다(새 id 부여, Markdown 으로 간주).
public static class VaultMarkdown
{
    public static string ToMarkdown(Note note)
    {
        var (format, body) = note.Format == NoteContentFormat.RichTextXaml
            ? (NoteContentFormat.PlainText, note.PlainText ?? string.Empty)
            : (note.Format, note.Content ?? string.Empty);

        var sb = new StringBuilder();
        sb.Append("---\n");
        sb.Append("id: ").Append(note.Id.ToString()).Append('\n');
        sb.Append("title: ").Append(YamlScalar(note.Title)).Append('\n');
        sb.Append("format: ").Append(format).Append('\n');
        sb.Append("color: ").Append(note.Color).Append('\n');
        if (note.Tags is { Count: > 0 })
        {
            sb.Append("tags: [").Append(string.Join(", ", note.Tags.Select(YamlScalar))).Append("]\n");
        }
        sb.Append("created: ").Append(note.CreatedAt.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture)).Append('\n');
        sb.Append("modified: ").Append(note.ModifiedAt.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture)).Append('\n');
        sb.Append("---\n\n");
        sb.Append(body.Replace("\r\n", "\n").TrimEnd()).Append('\n');
        return sb.ToString();
    }

    /// `.md` 텍스트를 노트로 파싱한다. 프런트매터가 없거나 불완전해도 최선을 다해 노트를 만든다.
    public static Note FromMarkdown(string content, string? fallbackTitle = null) =>
        FromMarkdown(content, fallbackTitle, out _);

    /// <paramref name="hadFrontmatterId"/>: 프런트매터에 유효한 id 가 있었는지 — 없으면 호출자
    /// (볼트 로드)가 파일명 기반의 안정적 id 를 부여해 리로드마다 id 가 바뀌는 진동을 막는다.
    public static Note FromMarkdown(string content, string? fallbackTitle, out bool hadFrontmatterId)
    {
        content = (content ?? string.Empty).Replace("\r\n", "\n");
        var front = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var body = content;

        // 프런트매터: 첫 줄이 "---" 이고 다음 "---" 로 닫힐 때만.
        if (content.StartsWith("---\n", StringComparison.Ordinal))
        {
            var end = content.IndexOf("\n---", 4, StringComparison.Ordinal);
            if (end >= 0)
            {
                var block = content.Substring(4, end - 4);
                foreach (var line in block.Split('\n'))
                {
                    var idx = line.IndexOf(':');
                    if (idx <= 0) continue;
                    var key = line[..idx].Trim();
                    var val = line[(idx + 1)..].Trim();
                    if (key.Length > 0) front[key] = val;
                }
                // 닫는 "---" 다음 줄부터가 본문.
                var after = end + 4; // "\n---"
                var nl = content.IndexOf('\n', after);
                body = nl >= 0 ? content[(nl + 1)..] : string.Empty;
            }
        }

        var format = front.TryGetValue("format", out var f) && Enum.TryParse<NoteContentFormat>(f, true, out var fmt)
            ? fmt
            : NoteContentFormat.Markdown; // 볼트 파일 기본은 Markdown
        // ToMarkdown 이 본문을 TrimEnd()+'\n' 으로 쓰므로 파싱도 같은 정규형으로 —
        // 왕복이 멱등이 되어 저장 직후 리로드가 유령 변경(diff)을 만들지 않는다.
        var trimmedBody = body.TrimStart('\n').TrimEnd();

        var note = new Note
        {
            Content = trimmedBody,
            Format = format,
            Color = front.TryGetValue("color", out var c) && Enum.TryParse<NoteColor>(c, true, out var col)
                ? col : NoteColor.Yellow,
            CreatedAt = ParseDate(front, "created") ?? DateTime.UtcNow,
            ModifiedAt = ParseDate(front, "modified") ?? DateTime.UtcNow,
        };

        hadFrontmatterId = false;
        if (front.TryGetValue("id", out var idStr) && Guid.TryParse(idStr, out var id))
        {
            note.Id = id;
            hadFrontmatterId = true;
        }
        // else: Note.Id 기본값(새 Guid) — 볼트 로드는 hadFrontmatterId 로 파일명 기반 안정 id 를 덧입힌다.

        note.PlainText = TextExtraction.ToPlainText(trimmedBody, format);
        // 프런트매터에 title 키가 있으면(빈 값 포함) 그 값이 정본 — 빈 제목이 파일명으로
        // 둔갑해 왕복마다 유령 변경을 만들지 않게 한다. 키가 없을 때만 본문/파일명에서 유도.
        note.Title = front.TryGetValue("title", out var rawTitle)
            ? UnquoteYaml(rawTitle) ?? string.Empty
            : (TextExtraction.DeriveTitle(note.PlainText) is { Length: > 0 } d ? d : (fallbackTitle ?? string.Empty));
        note.Tags = ParseTags(front.GetValueOrDefault("tags"), note.PlainText);
        return note;
    }

    private static List<string> ParseTags(string? tagsLine, string plainText)
    {
        // 프런트매터 tags 가 있으면 그걸, 없으면 본문에서 #태그 추출.
        if (!string.IsNullOrWhiteSpace(tagsLine))
        {
            var inner = tagsLine.Trim().TrimStart('[').TrimEnd(']');
            var list = inner.Split(',')
                .Select(s => UnquoteYaml(s.Trim()))
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s!.TrimStart('#'))
                .ToList();
            if (list.Count > 0) return list;
        }
        return TextExtraction.ExtractTags(plainText).ToList();
    }

    private static DateTime? ParseDate(IDictionary<string, string> front, string key) =>
        front.TryGetValue(key, out var s) &&
        DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d)
            ? d : null;

    private static readonly char[] YamlSpecials =
        { ':', '#', '"', '\'', '[', ']', '{', '}', ',', '&', '*', '!', '|', '>', '%', '@', '`' };

    private static string YamlScalar(string? value)
    {
        value = (value ?? string.Empty).Replace("\r", " ").Replace("\n", " ");
        if (value.Length == 0) return "\"\"";
        var needsQuote = value[0] == ' ' || value[^1] == ' ' || value.IndexOfAny(YamlSpecials) >= 0;
        return needsQuote ? "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"" : value;
    }

    private static string? UnquoteYaml(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            return value[1..^1].Replace("\\\"", "\"").Replace("\\\\", "\\");
        return value;
    }
}
