using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using StickyPad.Models;
using StickyPad.Resources;
using StickyPad.Services;
using StickyPad.Utils;

namespace StickyPad.ViewModels;

/// 노트 목록 창의 탭. Todos 는 모든 활성 노트의 체크박스를 모아 보는 통합 할 일 뷰(spec-2).
public enum NoteListViewMode { Active, Trash, Todos }

public sealed partial class NotesListViewModel : ObservableObject
{
    private readonly INoteRepository _repository;
    private readonly IWindowManager _windowManager;
    private readonly IBackupService _backupService;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<NotesListViewModel> _logger;
    private readonly List<Note> _activeRaw = new();
    private readonly List<Note> _trashedRaw = new();
    private readonly ObservableCollection<NoteSummary> _items = new();
    private readonly ObservableCollection<TagFilter> _tags = new();

    /// 내보내기용 선택 상태를 노트 id 로 보관 — 검색·필터로 목록이 재구성돼도 선택이 유지된다.
    private readonly HashSet<Guid> _selectedIds = new();

    public ObservableCollection<NoteSummary> Items => _items;
    public IReadOnlyList<TagFilter> Tags => _tags;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private int _selectedCount;

    public bool HasSelection => SelectedCount > 0;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string? _activeTag;

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private int _trashedCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowTrash))]
    [NotifyPropertyChangedFor(nameof(IsActiveView))]
    [NotifyPropertyChangedFor(nameof(IsTrashView))]
    [NotifyPropertyChangedFor(nameof(IsTodoView))]
    [NotifyPropertyChangedFor(nameof(EmptyMessage))]
    private NoteListViewMode _viewMode;

    public bool IsActiveView => ViewMode == NoteListViewMode.Active;

    /// 기존 XAML 바인딩 호환용 — Trash 여부를 bool 로 노출(설정 시 Active↔Trash 전환).
    public bool ShowTrash
    {
        get => ViewMode == NoteListViewMode.Trash;
        set => ViewMode = value ? NoteListViewMode.Trash : NoteListViewMode.Active;
    }

    public bool IsTrashView => ViewMode == NoteListViewMode.Trash;
    public bool IsTodoView => ViewMode == NoteListViewMode.Todos;

    public string EmptyMessage => ViewMode switch
    {
        NoteListViewMode.Trash => Strings.NoteList_EmptyTrash,
        NoteListViewMode.Todos => Strings.Todo_EmptyMessage,
        _ => string.IsNullOrWhiteSpace(SearchText) && string.IsNullOrEmpty(ActiveTag)
            ? Strings.NoteList_EmptyNoNotes
            : Strings.NoteList_EmptyNoMatch,
    };

    public NotesListViewModel(
        INoteRepository repository, IWindowManager windowManager, IBackupService backupService,
        ISettingsService settingsService, ILogger<NotesListViewModel> logger)
    {
        _repository = repository;
        _windowManager = windowManager;
        _backupService = backupService;
        _settingsService = settingsService;
        _logger = logger;
        _hideCompletedTodos = settingsService.Current.TodoHideCompleted;
    }

    partial void OnSearchTextChanged(string value)
    {
        RebuildItems();
        if (IsTodoView) RebuildTodos();
        OnPropertyChanged(nameof(EmptyMessage));
    }

    partial void OnActiveTagChanged(string? value)
    {
        RebuildItems();
        OnPropertyChanged(nameof(EmptyMessage));
    }

    partial void OnViewModeChanged(NoteListViewMode value)
    {
        // 휴지통/할일 모드에선 태그 필터를 초기화 (활성 탭 전용).
        if (value != NoteListViewMode.Active) ActiveTag = null;
        RebuildItems();
        if (value == NoteListViewMode.Todos) RebuildTodos();
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

            // 삭제되어 더는 활성이 아닌 노트의 선택은 정리 — 카운트와 내보내기 대상을 일치시킨다.
            _selectedIds.IntersectWith(_activeRaw.Select(n => n.Id));

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
            RebuildTodos();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load notes for list view");
        }
    }

    // ── 통합 할 일 뷰 (spec-2) ────────────────────────────────────────────────

    private readonly ObservableCollection<TodoGroup> _todoGroups = new();
    public ObservableCollection<TodoGroup> TodoGroups => _todoGroups;

    [ObservableProperty]
    private int _openTodoCount;   // 탭 배지: 전체 미완료 수(필터 무관)

    [ObservableProperty]
    private bool _hideCompletedTodos;

    partial void OnHideCompletedTodosChanged(bool value)
    {
        _settingsService.Current.TodoHideCompleted = value;
        _ = _settingsService.SaveAsync();
        RebuildTodos();
    }

    /// 체크박스 수집 대상: 활성 노트 중 PlainText/Markdown 만(리치·HTML 은 소스 치환이 불가능).
    private static bool IsTodoSource(Note n) =>
        n.Format is NoteContentFormat.PlainText or NoteContentFormat.Markdown;

    private void RebuildTodos()
    {
        var query = (SearchText ?? string.Empty).Trim();
        _todoGroups.Clear();
        var openTotal = 0;

        foreach (var note in _activeRaw.Where(IsTodoSource))
        {
            var tasks = TodoExtraction.ExtractTasks(note.Content);
            if (tasks.Count == 0) continue;
            openTotal += tasks.Count(t => !t.IsChecked);

            var theme = NotePalette.For(note.Color);
            var items = new ObservableCollection<TodoItemViewModel>();
            foreach (var t in tasks)
            {
                if (HideCompletedTodos && t.IsChecked) continue;
                if (query.Length > 0 && !t.Text.Contains(query, StringComparison.OrdinalIgnoreCase)) continue;
                items.Add(new TodoItemViewModel
                {
                    NoteId = note.Id,
                    LineIndex = t.LineIndex,
                    RawLine = t.RawLine,
                    Text = t.Text,
                    IsChecked = t.IsChecked,
                });
            }
            if (items.Count == 0) continue;

            var title = string.IsNullOrWhiteSpace(note.Title) ? Strings.NoteList_Untitled : note.Title;
            var group = new TodoGroup(note.Id, title, Frozen(theme.Header), Frozen(theme.Foreground), items);
            group.RefreshCount(tasks.Count(t => t.IsChecked), tasks.Count);
            _todoGroups.Add(group);
        }

        OpenTodoCount = openTotal;
        OnPropertyChanged(nameof(EmptyMessage));
    }

    [RelayCommand]
    private void OpenTodoNote(Guid noteId) => _windowManager.FocusNoteById(noteId);

    /// 할 일 체크/해제 — 라이브(열린 창) / 클로즈드(DB 직행) / 연동 파일 3경로(spec-2 §3.3).
    /// CheckBox 의 TwoWay 바인딩이 IsChecked 를 먼저 뒤집은 뒤 실행되므로 item.IsChecked 가 목표 상태다.
    [RelayCommand]
    private async Task ToggleTodoAsync(TodoItemViewModel? item)
    {
        if (item is null) return;
        var desired = item.IsChecked;
        try
        {
            // 1) 최신 소스 확보 — 열린 창이 있으면 디바운스 미저장분까지 포함한 라이브 VM 이 원본.
            if (!_windowManager.TryGetLiveNoteContent(item.NoteId, out var source, out _))
            {
                var fresh = await _repository.GetByIdAsync(item.NoteId).ConfigureAwait(true);
                if (fresh is null) { await ReloadAsync().ConfigureAwait(true); return; }
                source = fresh.Content;
            }

            // 2) 오프셋 1글자 토글. stale(그 사이 편집됨)이면 되돌리고 전체 재수집.
            var newContent = TodoExtraction.ToggleLine(source, item.LineIndex, item.RawLine, desired);
            if (newContent is null)
            {
                item.IsChecked = !desired;
                await ReloadAsync().ConfigureAwait(true);
                return;
            }
            if (string.Equals(newContent, source, StringComparison.Ordinal)) return;   // 이미 목표 상태

            // 3) 영속 — 라이브 경로(창의 에디터+VM 주입 → 디바운스 저장) 우선.
            if (!await _windowManager.TryUpdateLiveNoteContentAsync(item.NoteId, newContent).ConfigureAwait(true))
            {
                // 클로즈드 경로: DB 직행. 연동 노트면 원본 파일도 기록(안 하면 다음 열람 때 되덮임).
                var note = await _repository.GetByIdAsync(item.NoteId).ConfigureAwait(true);
                if (note is null) { await ReloadAsync().ConfigureAwait(true); return; }
                note.Content = newContent;
                note.PlainText = TextExtraction.ToPlainText(newContent, note.Format);
                if (string.IsNullOrEmpty(note.LinkedFilePath))
                {
                    note.Title = TextExtraction.DeriveTitle(note.PlainText);
                }
                note.Tags = TextExtraction.ExtractTags(note.PlainText);
                if (note.LinkedFilePath is { Length: > 0 } linkedPath)
                {
                    note.LinkedFileSyncedUtc = await LinkedFile.WriteAsync(linkedPath, newContent).ConfigureAwait(true);
                }
                await _repository.SaveContentAsync(note).ConfigureAwait(true);
            }

            // 4) 로컬 상태만 국소 갱신(전체 재수집 없이) — 캐시(_activeRaw)·항목·그룹 카운트.
            var cached = _activeRaw.FirstOrDefault(n => n.Id == item.NoteId);
            if (cached is not null)
            {
                cached.Content = newContent;
                cached.PlainText = TextExtraction.ToPlainText(newContent, cached.Format);
            }
            item.RawLine = TodoExtraction.ToggleLine(item.RawLine, 0, item.RawLine, desired) ?? item.RawLine;

            var group = _todoGroups.FirstOrDefault(g => g.NoteId == item.NoteId);
            if (group is not null)
            {
                var done = group.DoneCount + (desired ? 1 : -1);
                group.RefreshCount(done, group.TotalCount);
                if (HideCompletedTodos && desired) group.Items.Remove(item);
            }
            OpenTodoCount += desired ? -1 : 1;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Todo toggle failed for note {NoteId}", item.NoteId);
            item.IsChecked = !desired;   // 실패 시 체크 되돌림
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

            var title = string.IsNullOrWhiteSpace(note.Title) ? Strings.NoteList_Untitled : note.Title;
            var excerpt = TextExtraction.ExcerptOf(note.PlainText);
            var tagsLine = note.Tags.Count == 0 ? string.Empty : "#" + string.Join("  #", note.Tags);

            var match = SearchMatcher.Score(title, tagsLine, note.PlainText, tokens);
            if (!match.Matched) continue;

            var theme = NotePalette.For(note.Color);
            var summary = new NoteSummary(
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
                Score: match.Score);
            // 선택 상태 복원은 구독 전에 — 여기서의 세팅이 핸들러 부수효과를 일으키지 않게.
            summary.IsSelected = _selectedIds.Contains(note.Id);
            summary.PropertyChanged += OnSummarySelectionChanged;
            results.Add(summary);
        }

        IEnumerable<NoteSummary> ordered = tokens.Count == 0
            ? results.OrderByDescending(s => ShowTrash ? (s.DeletedAt ?? s.ModifiedAt) : s.ModifiedAt)
            : results.OrderByDescending(s => s.Score).ThenByDescending(s => s.ModifiedAt);

        _items.Clear();
        foreach (var item in ordered)
        {
            _items.Add(item);
        }
        UpdateSelectedCount();
    }

    private void OnSummarySelectionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(NoteSummary.IsSelected) || sender is not NoteSummary summary) return;
        if (summary.IsSelected) _selectedIds.Add(summary.Id);
        else _selectedIds.Remove(summary.Id);
        UpdateSelectedCount();
    }

    /// 선택된(검색으로 숨겨졌더라도) 노트 총수. 내보내기 대상과 항상 일치한다.
    private void UpdateSelectedCount() => SelectedCount = _selectedIds.Count;

    /// 현재 선택된 활성 노트의 id 목록 (검색으로 숨겨진 것 포함).
    private IReadOnlyList<Guid> SelectedActiveIds() =>
        _activeRaw.Where(n => _selectedIds.Contains(n.Id)).Select(n => n.Id).ToList();

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

    // ── 선택 & 내보내기 ───────────────────────────────────────────────────────

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var item in _items) item.IsSelected = true;
    }

    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var item in _items) item.IsSelected = false;
        _selectedIds.Clear();   // 검색으로 숨겨진 선택까지 모두 해제.
        UpdateSelectedCount();
    }

    [RelayCommand]
    private Task ExportMarkdownFiles() => ExportAsync(ExportFormat.MarkdownFiles);

    [RelayCommand]
    private Task ExportHtml() => ExportAsync(ExportFormat.Html);

    [RelayCommand]
    private Task ExportPdf() => ExportAsync(ExportFormat.Pdf);

    private async Task ExportAsync(ExportFormat format)
    {
        try
        {
            var ids = ExportTargetIds();
            if (ids.Count == 0) return;
            await _backupService.ExportNotesAsync(ids, format).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Export failed ({Format})", format);
        }
    }

    /// 선택된 노트가 있으면 그 노트들을(검색으로 숨겨진 것 포함, 목록 순서대로),
    /// 없으면 현재 보이는(필터된) 전체를 대상으로 한다.
    private IReadOnlyList<Guid> ExportTargetIds()
    {
        if (_selectedIds.Count > 0)
        {
            return _activeRaw.Where(n => _selectedIds.Contains(n.Id)).Select(n => n.Id).ToList();
        }
        return _items.Select(i => i.Id).ToList();
    }

    // ── 일괄 작업 ─────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task BulkDeleteAsync()
    {
        try
        {
            var ids = SelectedActiveIds();
            if (ids.Count == 0) return;

            var result = MessageBox.Show(
                string.Format(Strings.Bulk_DeleteConfirmFormat, ids.Count),
                "StickyPad",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.OK) return;

            foreach (var id in ids)
            {
                await _repository.DeleteAsync(id).ConfigureAwait(true);
            }

            _selectedIds.Clear();
            await ReloadAsync().ConfigureAwait(true);
            await _windowManager.ReloadAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bulk delete failed");
        }
    }

    [RelayCommand]
    private async Task BulkSetColorAsync(NoteColor color)
    {
        try
        {
            var ids = SelectedActiveIds();
            if (ids.Count == 0) return;

            foreach (var id in ids)
            {
                var note = _activeRaw.FirstOrDefault(n => n.Id == id);
                if (note is null) continue;
                note.Color = color;
                await _repository.SaveContentAsync(note).ConfigureAwait(true);
            }

            await ReloadAsync().ConfigureAwait(true);
            await _windowManager.ReloadAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bulk color change failed");
        }
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

/// 통합 할 일 뷰의 항목 — 원본 노트의 특정 줄 하나를 가리킨다.
public sealed partial class TodoItemViewModel : ObservableObject
{
    public required Guid NoteId { get; init; }
    public required string Text { get; init; }
    public int LineIndex { get; set; }
    public string RawLine { get; set; } = string.Empty;   // 토글 시 stale 대조용(성공 시 갱신)

    [ObservableProperty]
    private bool _isChecked;
}

/// 통합 할 일 뷰의 노트별 그룹(헤더 = 노트 색 액센트 + 제목 + 완료 카운트).
public sealed partial class TodoGroup : ObservableObject
{
    public TodoGroup(Guid noteId, string title, Brush accentBrush, Brush foregroundBrush,
        ObservableCollection<TodoItemViewModel> items)
    {
        NoteId = noteId;
        Title = title;
        AccentBrush = accentBrush;
        ForegroundBrush = foregroundBrush;
        Items = items;
    }

    public Guid NoteId { get; }
    public string Title { get; }
    public Brush AccentBrush { get; }
    public Brush ForegroundBrush { get; }
    public ObservableCollection<TodoItemViewModel> Items { get; }

    public int DoneCount { get; private set; }
    public int TotalCount { get; private set; }

    [ObservableProperty]
    private string _countText = string.Empty;

    public void RefreshCount(int done, int total)
    {
        DoneCount = done;
        TotalCount = total;
        CountText = string.Format(Strings.Todo_GroupCountFormat, done, total);
    }
}
