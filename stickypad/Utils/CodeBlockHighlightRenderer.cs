using System;
using System.Collections.Generic;
using System.Text;
using ColorCode;
using ColorCode.Styling;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using MdHtmlRenderer = Markdig.Renderers.HtmlRenderer;

namespace StickyPad.Utils;

/// 펜스 코드블록(```lang)을 ColorCode(인라인 스타일)로 하이라이트해 렌더한다.
/// 미지원 언어·정보 없음·비펜스 블록·하이라이터 실패는 Markdig 기본 렌더러로 폴백(기존 출력 그대로).
/// 스크립트 0줄 — 인라인 style 속성만 추가되므로 미리보기·내보내기의 CSP·IsScriptEnabled=false
/// 불변식이 그대로 유지된다(docs/SECURITY-REVIEW.md).
internal sealed class CodeBlockHighlightRenderer : HtmlObjectRenderer<CodeBlock>
{
    private static readonly CodeBlockRenderer Fallback = new();

    /// 마크다운 정보 문자열(```js 등) → ColorCode 언어 id. 없는 언어(yaml/bash 등)는 폴백.
    private static readonly Dictionary<string, string> AliasMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["js"] = "javascript", ["jsx"] = "javascript", ["node"] = "javascript", ["javascript"] = "javascript",
        ["json"] = "javascript",  // ColorCode 에 JSON 이 없어 JS 문법으로 근사(이스케이프 경로는 동일)
        ["ts"] = "typescript", ["tsx"] = "typescript", ["typescript"] = "typescript",
        ["py"] = "python", ["python"] = "python",
        ["cs"] = "csharp", ["c#"] = "csharp", ["csharp"] = "csharp",
        ["c"] = "cpp", ["cpp"] = "cpp", ["c++"] = "cpp", ["cc"] = "cpp", ["h"] = "cpp", ["hpp"] = "cpp",
        ["html"] = "html", ["htm"] = "html",
        ["css"] = "css",
        ["xml"] = "xml", ["xaml"] = "xml", ["csproj"] = "xml",
        ["sql"] = "sql",
        ["java"] = "java",
        ["ps1"] = "powershell", ["ps"] = "powershell", ["powershell"] = "powershell",
    };

    protected override void Write(MdHtmlRenderer renderer, CodeBlock block)
    {
        var language = FindLanguage(block);
        if (language is null)
        {
            ((IMarkdownObjectRenderer)Fallback).Write(renderer, block);   // 기존과 동일한 이스케이프 출력
            return;
        }

        string html;
        try
        {
            // ColorCode 가 코드 텍스트를 HTML 이스케이프하고 span 에 인라인 스타일만 입힌다.
            html = new HtmlFormatter(StyleDictionary.DefaultLight).GetHtmlString(ExtractSource(block), language);
        }
        catch (Exception)
        {
            ((IMarkdownObjectRenderer)Fallback).Write(renderer, block);   // 하이라이터 실패 시 안전 폴백
            return;
        }

        renderer.EnsureLine();
        renderer.WriteLine(html);
    }

    private static ILanguage? FindLanguage(CodeBlock block)
    {
        if (block is not FencedCodeBlock { Info.Length: > 0 } fenced) return null;
        // 정보 문자열의 첫 토큰만 언어로 취급("csharp title=x" 같은 확장 무시).
        var token = fenced.Info.Trim();
        var space = token.IndexOf(' ');
        if (space > 0) token = token[..space];
        return AliasMap.TryGetValue(token, out var id) ? Languages.FindById(id) : null;
    }

    private static string ExtractSource(CodeBlock block)
    {
        var sb = new StringBuilder();
        var lines = block.Lines;
        for (var i = 0; i < lines.Count; i++)
        {
            if (i > 0) sb.Append('\n');
            sb.Append(lines.Lines[i].Slice.ToString());
        }
        return sb.ToString();
    }
}
