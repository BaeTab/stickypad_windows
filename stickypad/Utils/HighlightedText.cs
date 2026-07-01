using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using StickyPad.ViewModels;

namespace StickyPad.Utils;

/// TextBlock 의 Inlines 를 HighlightSegment 컬렉션으로 채워 넣는 attached property.
/// IsMatch=true 인 세그먼트만 굵게 처리. 검색 결과 카드에서 사용.
public static class HighlightedText
{
    public static readonly DependencyProperty SegmentsProperty =
        DependencyProperty.RegisterAttached(
            "Segments",
            typeof(IReadOnlyList<HighlightSegment>),
            typeof(HighlightedText),
            new PropertyMetadata(null, OnSegmentsChanged));

    public static IReadOnlyList<HighlightSegment>? GetSegments(DependencyObject d) =>
        (IReadOnlyList<HighlightSegment>?)d.GetValue(SegmentsProperty);

    public static void SetSegments(DependencyObject d, IReadOnlyList<HighlightSegment>? value) =>
        d.SetValue(SegmentsProperty, value);

    private static void OnSegmentsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock tb) return;
        tb.Inlines.Clear();
        if (e.NewValue is not IReadOnlyList<HighlightSegment> segs || segs.Count == 0) return;

        foreach (var seg in segs)
        {
            var run = new Run(seg.Text);
            if (seg.IsMatch)
            {
                run.FontWeight = FontWeights.Bold;
            }
            tb.Inlines.Add(run);
        }
    }
}
