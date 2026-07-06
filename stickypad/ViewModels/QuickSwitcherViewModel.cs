using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StickyPad.Models;
using StickyPad.Resources;
using StickyPad.Services;
using StickyPad.Utils;

namespace StickyPad.ViewModels;

/// 빠른 전환기(Ctrl+P) 팝업의 VM. 열릴 때마다 새로 생성되므로(WindowManager.OpenQuickSwitcher)
/// 스냅샷은 인스턴스 상태로 들고 있으면 충분하고 별도 캐시 무효화가 필요 없다.
public sealed partial class QuickSwitcherViewModel : ObservableObject
{
    private const int MaxResults = 20;

    private readonly INoteRepository _repository;
    private readonly IWindowManager _windowManager;
    private List<Note> _snapshot = new();

    public ObservableCollection<QuickSwitcherItem> Results { get; } = new();

    /// Enter/클릭으로 선택 노트를 연 뒤 팝업을 닫아 달라는 요청. 코드비하인드가 구독해 Close() 호출.
    public event Action? CloseRequested;

    [ObservableProperty]
    private string _queryText = string.Empty;

    [ObservableProperty]
    private int _selectedIndex = -1;

    public QuickSwitcherViewModel(INoteRepository repository, IWindowManager windowManager)
    {
        _repository = repository;
        _windowManager = windowManager;
    }

    public async Task LoadAsync()
    {
        var all = await _repository.GetAllAsync().ConfigureAwait(true);
        _snapshot = all.ToList();
        RebuildResults();
    }

    partial void OnQueryTextChanged(string value) => RebuildResults();

    public void MoveSelection(int delta)
    {
        if (Results.Count == 0)
        {
            SelectedIndex = -1;
            return;
        }
        var next = (SelectedIndex + delta) % Results.Count;
        if (next < 0) next += Results.Count;
        SelectedIndex = next;
    }

    [RelayCommand]
    private void OpenSelected()
    {
        if (SelectedIndex < 0 || SelectedIndex >= Results.Count) return;
        OpenNote(Results[SelectedIndex]);
    }

    [RelayCommand]
    private void OpenNote(QuickSwitcherItem? item)
    {
        if (item is null) return;
        _windowManager.FocusNoteById(item.NoteId);
        CloseRequested?.Invoke();
    }

    private void RebuildResults()
    {
        var query = QueryText ?? string.Empty;

        var matches = new List<(Note Note, string Title, string TagsLine, FuzzyMatcher.FuzzyResult Match)>();
        foreach (var note in _snapshot)
        {
            var title = string.IsNullOrWhiteSpace(note.Title) ? Strings.NoteList_Untitled : note.Title;
            var tagsLine = note.Tags.Count == 0 ? string.Empty : "#" + string.Join(" #", note.Tags);
            var target = tagsLine.Length == 0 ? title : title + " " + tagsLine;

            var match = FuzzyMatcher.Match(target, query, title.Length);
            if (!match.Matched) continue;

            matches.Add((note, title, tagsLine, match));
        }

        IEnumerable<(Note Note, string Title, string TagsLine, FuzzyMatcher.FuzzyResult Match)> ordered =
            string.IsNullOrWhiteSpace(query)
                ? matches.OrderByDescending(m => m.Note.ModifiedAt)
                : matches.OrderByDescending(m => m.Match.Score).ThenByDescending(m => m.Note.ModifiedAt);

        Results.Clear();
        foreach (var (note, title, tagsLine, match) in ordered.Take(MaxResults))
        {
            var theme = NotePalette.For(note.Color);
            Results.Add(new QuickSwitcherItem(
                NoteId: note.Id,
                Title: title,
                TagsLine: tagsLine,
                AccentBrush: Frozen(theme.Header),
                TitleSegments: BuildSegments(title, 0, match.Positions),
                TagSegments: BuildSegments(tagsLine, title.Length + 1, match.Positions)));
        }

        SelectedIndex = Results.Count > 0 ? 0 : -1;
    }

    /// match.Positions 는 "제목 + ' ' + 태그라인" 합성 문자열 기준 인덱스이므로, 각 필드로 잘라내려면
    /// 그 필드가 합성 문자열에서 시작하는 offset 만큼 빼서 로컬 인덱스로 바꾼다.
    private static IReadOnlyList<HighlightSegment> BuildSegments(string text, int offset, IReadOnlyList<int> positions)
    {
        if (string.IsNullOrEmpty(text)) return new[] { new HighlightSegment(text ?? string.Empty, false) };

        var matchSet = new HashSet<int>();
        foreach (var pos in positions)
        {
            var local = pos - offset;
            if (local >= 0 && local < text.Length) matchSet.Add(local);
        }
        if (matchSet.Count == 0) return new[] { new HighlightSegment(text, false) };

        var segments = new List<HighlightSegment>();
        var start = 0;
        var currentIsMatch = matchSet.Contains(0);
        for (var i = 1; i <= text.Length; i++)
        {
            var isMatch = i < text.Length && matchSet.Contains(i);
            if (i == text.Length || isMatch != currentIsMatch)
            {
                segments.Add(new HighlightSegment(text.Substring(start, i - start), currentIsMatch));
                start = i;
                currentIsMatch = isMatch;
            }
        }
        return segments;
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}

public sealed record QuickSwitcherItem(
    Guid NoteId,
    string Title,
    string TagsLine,
    Brush AccentBrush,
    IReadOnlyList<HighlightSegment> TitleSegments,
    IReadOnlyList<HighlightSegment> TagSegments);
