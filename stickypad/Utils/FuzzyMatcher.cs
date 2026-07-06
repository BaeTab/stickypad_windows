using System;
using System.Collections.Generic;

namespace StickyPad.Utils;

/// 빠른 전환기(Ctrl+P) 전용 퍼지(비연속 부분수열) 매칭. 토큰 단위 substring AND 인 기존
/// <see cref="SearchMatcher"/> 와 달리, 각 토큰이 대상 문자열의 부분수열이기만 하면 매칭된다.
public static class FuzzyMatcher
{
    public readonly record struct FuzzyResult(bool Matched, double Score, IReadOnlyList<int> Positions);

    private const double ConsecutiveRunBonus = 3.0;
    private const double WordStartBonus = 4.0;
    private const double TitleWeight = 2.0;
    private const double FirstMatchPenaltyPerIndex = 0.1;

    /// 공백으로 토큰화한 각 쿼리 토큰이 <paramref name="target"/> 의 부분수열로 매칭돼야 함(AND).
    /// <paramref name="titleLength"/> 를 주면 그 이전 인덱스(제목 영역)의 매칭 글자에 가중치를 준다.
    public static FuzzyResult Match(string target, string query, int titleLength = -1)
    {
        target ??= string.Empty;
        var tokens = Tokenize(query);
        if (tokens.Count == 0)
        {
            return new FuzzyResult(true, 0, Array.Empty<int>());
        }

        var allPositions = new SortedSet<int>();
        double score = 0;
        int? firstMatchIndex = null;

        foreach (var token in tokens)
        {
            if (!TryMatchSubsequence(target, token, out var positions))
            {
                return new FuzzyResult(false, 0, Array.Empty<int>());
            }

            for (var i = 0; i < positions.Count; i++)
            {
                var pos = positions[i];
                allPositions.Add(pos);

                var inTitle = titleLength >= 0 && pos < titleLength;
                score += inTitle ? TitleWeight : 1.0;

                if (i > 0 && positions[i] == positions[i - 1] + 1)
                {
                    score += ConsecutiveRunBonus;
                }

                if (IsWordStart(target, pos))
                {
                    score += WordStartBonus;
                }

                if (firstMatchIndex is null || pos < firstMatchIndex)
                {
                    firstMatchIndex = pos;
                }
            }
        }

        if (firstMatchIndex is { } idx)
        {
            score -= FirstMatchPenaltyPerIndex * idx;
        }

        return new FuzzyResult(true, score, new List<int>(allPositions));
    }

    private static IReadOnlyList<string> Tokenize(string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return Array.Empty<string>();
        return query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// 탐욕적(greedy) 좌→우 부분수열 매칭: 토큰의 각 글자를 target 에서 가장 이른 위치부터 순서대로 찾는다.
    private static bool TryMatchSubsequence(string target, string token, out List<int> positions)
    {
        positions = new List<int>(token.Length);
        var ti = 0;
        foreach (var ch in token)
        {
            var found = false;
            for (; ti < target.Length; ti++)
            {
                if (char.ToUpperInvariant(target[ti]) == char.ToUpperInvariant(ch))
                {
                    positions.Add(ti);
                    ti++;
                    found = true;
                    break;
                }
            }
            if (!found) return false;
        }
        return true;
    }

    /// 공백·'#'·구두점 뒤(또는 문자열 맨 앞)를 단어 시작으로 취급.
    private static bool IsWordStart(string target, int index)
    {
        if (index <= 0) return true;
        var prev = target[index - 1];
        return char.IsWhiteSpace(prev) || prev == '#' || char.IsPunctuation(prev);
    }
}
