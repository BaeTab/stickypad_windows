using System;
using System.Collections.Generic;
using System.Windows.Media;
using StickyPad.Models;

namespace StickyPad.ViewModels;

/// 검색 하이라이트 단위. IsMatch=true 인 세그먼트는 굵게 그려진다.
public sealed record HighlightSegment(string Text, bool IsMatch);

public sealed record NoteSummary(
    Guid Id,
    string Title,
    string Excerpt,
    string PlainText,
    string TagsLine,
    NoteColor Color,
    Brush AccentBrush,
    Brush ForegroundBrush,
    DateTime ModifiedAt,
    bool IsTrashed,
    DateTime? DeletedAt,
    IReadOnlyList<HighlightSegment> TitleSegments,
    IReadOnlyList<HighlightSegment> ExcerptSegments,
    IReadOnlyList<HighlightSegment> TagSegments,
    double Score);
