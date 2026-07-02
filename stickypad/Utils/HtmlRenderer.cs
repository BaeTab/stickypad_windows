using System.Net;
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

    private static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
}
