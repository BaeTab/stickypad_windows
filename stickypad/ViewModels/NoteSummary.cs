using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Media;
using StickyPad.Models;

namespace StickyPad.ViewModels;

/// 검색 하이라이트 단위. IsMatch=true 인 세그먼트는 굵게 그려진다.
public sealed record HighlightSegment(string Text, bool IsMatch);

/// 목록 카드 1장의 표시 모델. 카드마다 토글되는 가변 IsSelected 를 담으므로
/// record(값 동등성)가 아닌 class(참조 동등성)다 — 값 동등성이면 IsSelected 가
/// GetHashCode 에 섞여 WPF Selector 의 해시 기반 조회가 깨질 수 있다.
public sealed class NoteSummary(
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
    double Score) : INotifyPropertyChanged
{
    public Guid Id { get; } = Id;
    public string Title { get; } = Title;
    public string Excerpt { get; } = Excerpt;
    public string PlainText { get; } = PlainText;
    public string TagsLine { get; } = TagsLine;
    public NoteColor Color { get; } = Color;
    public Brush AccentBrush { get; } = AccentBrush;
    public Brush ForegroundBrush { get; } = ForegroundBrush;
    public DateTime ModifiedAt { get; } = ModifiedAt;
    public bool IsTrashed { get; } = IsTrashed;
    public DateTime? DeletedAt { get; } = DeletedAt;
    public IReadOnlyList<HighlightSegment> TitleSegments { get; } = TitleSegments;
    public IReadOnlyList<HighlightSegment> ExcerptSegments { get; } = ExcerptSegments;
    public IReadOnlyList<HighlightSegment> TagSegments { get; } = TagSegments;
    public double Score { get; } = Score;

    private bool _isSelected;

    /// 목록에서 체크박스로 고른 상태. 내보내기 대상 선택에 쓰인다.
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, IsSelectedChangedArgs);
        }
    }

    private static readonly PropertyChangedEventArgs IsSelectedChangedArgs = new(nameof(IsSelected));

    public event PropertyChangedEventHandler? PropertyChanged;
}
