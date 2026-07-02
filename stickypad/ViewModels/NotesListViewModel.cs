using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using StickyPad.Models;
using StickyPad.Services;
using StickyPad.Utils;

namespace StickyPad.ViewModels;

public sealed partial class NotesListViewModel : ObservableObject
{
    private readonly INoteRepository _repository;
    private readonly IWindowManager _windowManager;
    private readonly IBackupService _backupService;
    private readonly ILogger<NotesListViewModel> _logger;
    private readonly List<Note> _activeRaw = new();
    private readonly List<Note> _trashedRaw = new();
    private readonly ObservableCollection<NoteSummary> _items = new();
    private readonly ObservableCollection<TagFilter> _tags = new();

    public ObservableCollection<NoteSummary> Items => _items;
    public IReadOnlyList<TagFilter> Tags => _tags;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string? _activeTag;

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private int _trashedCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTrashView))]
    [NotifyPropertyChangedFor(nameof(EmptyMessage))]
    private bool _showTrash;

    public bool IsTrashView => ShowTrash;

    public string EmptyMessage => ShowTrash
        ? "휴지통이 비어 있습니다."
        : (string.IsNullOrWhiteSpace(SearchText) && string.IsNullOrEmpty(ActiveTag)
            ? "아직 노트가 없습니다. 새 노트를 만들어 보세요."
            : "검색·필터에 일치하는 노트가 없습니다.");

    public NotesListViewModel(INoteRepository repository, IWindowManager windowManager, IBackupService backupService, ILogger<NotesListViewModel> logger)
    {
        _repository = repository;
        _windowManager = windowManager;
        _backupService = backupService;
        _logger = logger;
    }

    partial void OnSearchTextChanged(string value)
    {
        RebuildItems();
        OnPropertyChanged(nameof(EmptyMessage));
    }

    partial void OnActiveTagChanged(string? value)
    {
        RebuildItems();
        OnPropertyChanged(nameof(EmptyMessage));
    }

    partial void OnShowTrashChanged(bool value)
    {
        // 휴지통 모드에선 태그 필터를 초기화 (휴지통은 태그 인덱스를 갱신하지 않음).
        if (value) ActiveTag = null;
        RebuildItems();
        OnPropertyChanged(nameof(EmptyMessage));
    }

    public async Task ReloadAsync()
    {
        try
        {
            var active = await _repository.GetAllAsync().ConfigureAwait(true);
            var trashed = await _repository.GetTrashedAsync().ConfigureAwait(true);

            _activeRaw.Clear();
            _activeRaw.AddRange(active);
            _trashedRaw.Clear();
            _trashedRaw.AddRange(trashed);

            // 태그 카운트는 활성 노트만 대상으로.
            var tagCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var note in active)
            {
                foreach (var tag in note.Tags)
                {
                    tagCounts[tag] = tagCounts.TryGetValue(tag, out var c) ? c + 1 : 1;
                }
            }
            _tags.Clear();
            foreach (var (tag, count) in tagCounts.OrderByDescending(p => p.Value).ThenBy(p => p.Key))
            {
                _tags.Add(new TagFilter(tag, count));
            }

            TotalCount = active.Count;
            TrashedCount = trashed.Count;
            RebuildItems();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load notes for list view");
        }
    }

    private void RebuildItems()
    {
        var source = ShowTrash ? _trashedRaw : _activeRaw;
        var tokens = SearchMatcher.Tokenize(SearchText ?? string.Empty);

        var results = new List<NoteSummary>();
        foreach (var note in source)
        {
            // 태그 필터 (활성 모드 한정)
            if (!ShowTrash && !string.IsNullOrEmpty(ActiveTag))
            {
                if (!note.Tags.Any(t => string.Equals(t, ActiveTag, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }
            }

            var title = string.IsNullOrWhiteSpace(note.Title) ? "(untitled)" : note.Title;
            var excerpt = TextExtraction.ExcerptOf(note.PlainText);
            var tagsLine = note.Tags.Count == 0 ? string.Empty : "#" + string.Join("  #", note.Tags);

            var match = SearchMatcher.Score(title, tagsLine, note.PlainText, tokens);
            if (!match.Matched) continue;

            var theme = NotePalette.For(note.Color);
            results.Add(new NoteSummary(
                Id: note.Id,
                Title: title,
                Excerpt: excerpt,
                PlainText: note.PlainText,
                TagsLine: tagsLine,
                Color: note.Color,
                AccentBrush: Frozen(theme.Header),
                ForegroundBrush: Frozen(theme.Foreground),
                ModifiedAt: note.ModifiedAt,
                IsTrashed: note.IsDeleted,
                DeletedAt: note.DeletedAt,
                TitleSegments: SearchMatcher.Highlight(title, tokens),
                ExcerptSegments: SearchMatcher.Highlight(excerpt, tokens),
                TagSegments: SearchMatcher.Highlight(tagsLine, tokens),
                Score: match.Score));
        }

        IEnumerable<NoteSummary> ordered = tokens.Count == 0
            ? results.OrderByDescending(s => ShowTrash ? (s.DeletedAt ?? s.ModifiedAt) : s.ModifiedAt)
            : results.OrderByDescending(s => s.Score).ThenByDescending(s => s.ModifiedAt);

        _items.Clear();
        foreach (var item in ordered)
        {
            _items.Add(item);
        }
    }

    [RelayCommand]
    private void Open(NoteSummary? summary)
    {
        if (summary is null || summary.IsTrashed) return;
        _windowManager.FocusNoteById(summary.Id);
    }

    [RelayCommand]
    private async Task DeleteAsync(NoteSummary? summary)
    {
        if (summary is null) return;
        await _repository.DeleteAsync(summary.Id).ConfigureAwait(true);
        await ReloadAsync().ConfigureAwait(true);
        await _windowManager.ReloadAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task RestoreAsync(NoteSummary? summary)
    {
        if (summary is null) return;
        await _repository.RestoreAsync(summary.Id).ConfigureAwait(true);
        await ReloadAsync().ConfigureAwait(true);
        await _windowManager.ReloadAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task PurgeAsync(NoteSummary? summary)
    {
        if (summary is null) return;
        await _repository.PurgeAsync(summary.Id).ConfigureAwait(true);
        await ReloadAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task EmptyTrashAsync()
    {
        var ids = _trashedRaw.Select(n => n.Id).ToList();
        foreach (var id in ids)
        {
            await _repository.PurgeAsync(id).ConfigureAwait(true);
        }
        await ReloadAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private void NewNote() => _windowManager.CreateAndShowNew();

    [RelayCommand]
    private async Task ExportTextAsync()
    {
        try { await _backupService.ExportNotesAsTextAsync().ConfigureAwait(true); }
        catch (Exception ex) { _logger.LogError(ex, "Failed to export notes as text"); }
    }

    [RelayCommand]
    private void ClearTag() => ActiveTag = null;

    [RelayCommand]
    private void SelectTag(string? tag) => ActiveTag = tag;

    [RelayCommand]
    private void ToggleTrashView() => ShowTrash = !ShowTrash;

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}

public sealed record TagFilter(string Tag, int Count);
