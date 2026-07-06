using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace StickyPad.Utils;

/// 통합 할 일 뷰의 순수 코어: PlainText/Markdown 소스에서 체크박스 라인을 추출하고,
/// 마크 문자 1글자만 절대 오프셋으로 치환해 토글한다(spec-2 §3.2).
///
/// 사용자 노트 원문을 변형하는 유일한 로직이므로 규칙이 엄격하다:
/// - split/join 을 쓰지 않는다 — CRLF/LF 혼합·말미 개행이 바이트 단위로 보존된다.
/// - 토글 전 호출측이 들고 있던 RawLine 과 현재 줄을 대조(낙관적 동시성) — 그 사이 노트가
///   편집되어 줄이 달라졌으면 null 을 돌려주고, 호출측은 재수집으로 복구한다.
public static class TodoExtraction
{
    /// <param name="LineIndex">0 기준 줄 번호(\n 기준 분할).</param>
    /// <param name="IsChecked">[x]/[X] 여부.</param>
    /// <param name="Text">체크박스 뒤 표시용 텍스트(트림됨).</param>
    /// <param name="RawLine">꼬리 \r 을 제거한 줄 원문 — 토글 시 stale 대조용.</param>
    public readonly record struct TodoLine(int LineIndex, bool IsChecked, string Text, string RawLine);

    // `- [ ] 텍스트` / `* [x] ...` / `+ [X] ...`, 들여쓰기(중첩) 허용.
    private static readonly Regex TaskLine = new(
        @"^(\s*)([-*+])\s+\[( |x|X)\]\s?(.*)$", RegexOptions.Compiled);

    /// 소스에서 체크박스 라인 전부 추출. 대상 포맷 검증(PlainText/Markdown만)은 호출측 책임.
    public static IReadOnlyList<TodoLine> ExtractTasks(string? source)
    {
        var result = new List<TodoLine>();
        if (string.IsNullOrEmpty(source)) return result;

        var lineIndex = 0;
        var start = 0;
        while (start <= source.Length)
        {
            var nl = source.IndexOf('\n', start);
            var end = nl < 0 ? source.Length : nl;
            var line = source[start..end].TrimEnd('\r');

            var m = TaskLine.Match(line);
            if (m.Success)
            {
                result.Add(new TodoLine(
                    lineIndex,
                    IsChecked: m.Groups[3].Value is "x" or "X",
                    Text: m.Groups[4].Value.Trim(),
                    RawLine: line));
            }

            if (nl < 0) break;
            start = nl + 1;
            lineIndex++;
        }
        return result;
    }

    /// lineIndex 줄이 expectedRaw 와 일치하면 마크 문자([ ]↔[x]) 1글자만 치환한 새 소스를
    /// 반환한다. 줄이 달라졌거나 범위 밖이거나 체크박스 줄이 아니면 null(호출측 재수집).
    public static string? ToggleLine(string? source, int lineIndex, string expectedRaw, bool check)
    {
        if (string.IsNullOrEmpty(source) || lineIndex < 0) return null;

        // lineIndex 줄의 시작 절대 오프셋을 \n 스캔으로 찾는다.
        var lineStart = 0;
        for (var i = 0; i < lineIndex; i++)
        {
            var nl = source.IndexOf('\n', lineStart);
            if (nl < 0) return null;                      // 범위 밖
            lineStart = nl + 1;
        }
        var lineEnd = source.IndexOf('\n', lineStart);
        if (lineEnd < 0) lineEnd = source.Length;

        var raw = source[lineStart..lineEnd].TrimEnd('\r');
        if (!string.Equals(raw, expectedRaw, StringComparison.Ordinal)) return null;   // stale

        var m = TaskLine.Match(raw);
        if (!m.Success) return null;

        var current = m.Groups[3].Value is "x" or "X";
        if (current == check) return source;              // 이미 원하는 상태 — 무변경

        // 마크 문자([ ] 안의 한 글자)의 절대 오프셋 = 줄 시작 + 그룹3의 줄 내 위치.
        var markOffset = lineStart + m.Groups[3].Index;
        return string.Concat(
            source.AsSpan(0, markOffset),
            check ? "x" : " ",
            source.AsSpan(markOffset + 1));
    }
}
