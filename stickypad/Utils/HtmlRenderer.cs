using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Windows.Media;
using Markdig;
using StickyPad.Models;

namespace StickyPad.Utils;

/// Turns a note's raw source (Markdown or HTML) into a themed HTML document for WebView2.
public static class HtmlRenderer
{
    private static readonly MarkdownPipeline Pipeline =
        new MarkdownPipelineBuilder().UseAdvancedExtensions().UseSoftlineBreakAsHardlineBreak().Build();

    public static string Render(string? source, NoteContentFormat format, NoteTheme theme)
    {
        source ??= string.Empty;
        var body = format switch
        {
            NoteContentFormat.Markdown => Markdown.ToHtml(source, Pipeline),
            NoteContentFormat.Html => source,
            _ => "<pre>" + WebUtility.HtmlEncode(source) + "</pre>",
        };
        return Wrap(body, theme);
    }

    private static string Wrap(string body, NoteTheme theme)
    {
        var bg = Hex(theme.Background);
        var fg = Hex(theme.Foreground);
        var accent = Hex(theme.Header);
        return
            "<!DOCTYPE html><html><head><meta charset=\"utf-8\">" +
            // Defense-in-depth: no scripts/objects/frames; allow only inline styles and data/https images & fonts.
            "<meta http-equiv=\"Content-Security-Policy\" content=\"" +
            "default-src 'none'; img-src data: https: http:; media-src data: https: http:; " +
            "style-src 'unsafe-inline'; font-src data: https:;\">" +
            "<meta name=\"color-scheme\" content=\"light dark\">" +
            "<style>" +
            $"html,body{{margin:0;padding:10px 12px;background:{bg};color:{fg};" +
            "font-family:'Segoe UI',system-ui,sans-serif;font-size:14px;line-height:1.55;" +
            "word-wrap:break-word;overflow-wrap:break-word;}" +
            "a{color:#1565C0;}" +
            "code{background:rgba(0,0,0,0.06);padding:1px 4px;border-radius:3px;font-family:Consolas,'Cascadia Mono',monospace;}" +
            "pre{background:rgba(0,0,0,0.06);padding:8px 10px;border-radius:6px;overflow:auto;}" +
            "pre code{background:none;padding:0;}" +
            "h1,h2,h3,h4{margin:.5em 0 .3em;line-height:1.25;}h1{font-size:1.5em;}h2{font-size:1.3em;}h3{font-size:1.12em;}" +
            $"blockquote{{margin:.5em 0;padding:.2em .8em;border-left:3px solid {accent};opacity:.9;}}" +
            "table{border-collapse:collapse;}th,td{border:1px solid rgba(0,0,0,0.25);padding:4px 8px;}" +
            "img{max-width:100%;height:auto;}ul,ol{padding-left:1.4em;}" +
            "hr{border:none;border-top:1px solid rgba(0,0,0,0.2);}" +
            "input[type=checkbox]{margin-right:.4em;}" +
            "</style></head><body>" + body + "</body></html>";
    }

    // ── 여러 노트를 하나의 인쇄·공유용 문서로 ──────────────────────────────────

    /// 선택한 노트들을 스타일이 적용된 단일 HTML 문서로 렌더한다. HTML 내보내기와 PDF
    /// 내보내기(오프스크린 WebView2 인쇄) 모두 이 결과를 그대로 사용한다.
    /// 각 노트는 제목·메타(태그·수정일)·본문 섹션으로 렌더되고, 노트 색이 좌측 강조 막대가 된다.
    public static string RenderDocument(IReadOnlyList<Note> notes, string documentTitle)
    {
        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html><html><head><meta charset=\"utf-8\">");
        // 노트 렌더와 동일한 방어적 CSP — 스크립트/오브젝트/프레임 없음, 인라인 스타일과 이미지·폰트만.
        sb.Append("<meta http-equiv=\"Content-Security-Policy\" content=\"")
          .Append("default-src 'none'; img-src data: https: http:; media-src data: https: http:; ")
          .Append("style-src 'unsafe-inline'; font-src data: https:;\">");
        sb.Append("<meta name=\"color-scheme\" content=\"light\">");
        sb.Append("<title>").Append(WebUtility.HtmlEncode(documentTitle)).Append("</title>");
        sb.Append("<style>").Append(DocumentCss).Append("</style></head><body>");

        sb.Append("<header class=\"doc-head\"><h1>")
          .Append(WebUtility.HtmlEncode(documentTitle)).Append("</h1>");
        sb.Append("<div class=\"doc-meta\">")
          .Append(WebUtility.HtmlEncode($"내보낸 시각 {DateTime.Now:yyyy-MM-dd HH:mm} · {notes.Count}개 노트"))
          .Append("</div></header>");

        foreach (var note in notes)
        {
            var theme = NotePalette.For(note.Color);
            var accent = Hex(theme.Header);
            var title = string.IsNullOrWhiteSpace(note.Title) ? "(제목 없음)" : note.Title;

            var body = note.Format switch
            {
                NoteContentFormat.Markdown => Markdown.ToHtml(note.Content ?? string.Empty, Pipeline),
                NoteContentFormat.Html => note.Content ?? string.Empty,
                // PlainText / RichTextXaml 은 사람이 읽을 수 있는 PlainText 투영을 그대로 이스케이프.
                _ => "<pre>" + WebUtility.HtmlEncode((note.PlainText ?? string.Empty).TrimEnd()) + "</pre>",
            };

            sb.Append("<article class=\"note\" style=\"border-left-color:").Append(accent).Append("\">");
            sb.Append("<h2 class=\"note-title\">").Append(WebUtility.HtmlEncode(title)).Append("</h2>");

            sb.Append("<div class=\"note-meta\">");
            if (note.Tags is { Count: > 0 })
            {
                foreach (var tag in note.Tags)
                {
                    sb.Append("<span class=\"tag\">#").Append(WebUtility.HtmlEncode(tag)).Append("</span>");
                }
            }
            sb.Append("<span class=\"date\">")
              .Append(WebUtility.HtmlEncode($"{note.ModifiedAt.ToLocalTime():yyyy-MM-dd HH:mm}"))
              .Append("</span></div>");

            sb.Append("<div class=\"note-body\">").Append(body).Append("</div>");
            sb.Append("</article>");
        }

        sb.Append("</body></html>");
        return sb.ToString();
    }

    private const string DocumentCss =
        "*{box-sizing:border-box;}" +
        "html,body{margin:0;padding:0;background:#f4f4f5;color:#1f2328;" +
        "font-family:'Segoe UI',system-ui,sans-serif;font-size:14px;line-height:1.6;}" +
        ".doc-head{max-width:820px;margin:0 auto;padding:28px 24px 8px;}" +
        ".doc-head h1{margin:0;font-size:1.7em;}" +
        ".doc-meta{color:#6b7280;font-size:.85em;margin-top:4px;}" +
        ".note{max-width:820px;margin:16px auto;padding:18px 22px;background:#fff;" +
        "border:1px solid #e5e7eb;border-left-width:5px;border-radius:8px;box-shadow:0 1px 2px rgba(0,0,0,.04);}" +
        ".note-title{margin:0 0 6px;font-size:1.25em;}" +
        ".note-meta{display:flex;flex-wrap:wrap;gap:6px;align-items:center;margin-bottom:12px;font-size:.8em;}" +
        ".note-meta .tag{color:#1d4ed8;background:#eff6ff;padding:1px 7px;border-radius:10px;}" +
        ".note-meta .date{color:#9ca3af;margin-left:auto;}" +
        ".note-body{word-wrap:break-word;overflow-wrap:break-word;}" +
        ".note-body pre{white-space:pre-wrap;background:rgba(0,0,0,.05);padding:10px 12px;border-radius:6px;overflow:auto;}" +
        ".note-body code{background:rgba(0,0,0,.06);padding:1px 4px;border-radius:3px;font-family:Consolas,'Cascadia Mono',monospace;}" +
        ".note-body pre code{background:none;padding:0;}" +
        ".note-body h1,.note-body h2,.note-body h3,.note-body h4{line-height:1.25;margin:.6em 0 .3em;}" +
        ".note-body img{max-width:100%;height:auto;}" +
        ".note-body table{border-collapse:collapse;}" +
        ".note-body th,.note-body td{border:1px solid rgba(0,0,0,.2);padding:4px 8px;}" +
        ".note-body blockquote{margin:.5em 0;padding:.2em .9em;border-left:3px solid #cbd5e1;color:#475569;}" +
        ".note-body a{color:#1565c0;}" +
        "@media print{body{background:#fff;}" +
        ".note{box-shadow:none;page-break-inside:avoid;break-inside:avoid;margin:0 auto 14px;}" +
        ".doc-head{padding-top:0;}}";

    private static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
}
