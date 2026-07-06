using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StickyPad.Models;

namespace StickyPad.Services;

/// 볼트 감시자가 소비하는 외부 변경 diff. Removed 는 미저장 편집 판정을 위해 이전 내용을 함께 든다.
public sealed record VaultDiff(
    IReadOnlyList<Note> Added,
    IReadOnlyList<(Guid Id, string OldContent)> Removed,
    IReadOnlyList<(Note Fresh, string OldContent)> Changed)
{
    public static readonly VaultDiff Empty = new(
        Array.Empty<Note>(), Array.Empty<(Guid, string)>(), Array.Empty<(Note, string)>());

    public bool IsEmpty => Added.Count == 0 && Removed.Count == 0 && Changed.Count == 0;
}

/// 볼트 폴더(.md 집합)를 저장소로 쓰는 <see cref="INoteRepository"/> 구현(2.0 라이브 볼트 모드).
/// 노트는 인메모리 캐시로 유지하고, 변경마다 해당 노트 하나만 폴더에 기록한다(<see cref="VaultStore.SaveOne"/>).
/// 기본 저장소는 여전히 LiteDB 이며, 볼트 모드는 설정에서 옵트인한다(기존 데이터 위험 없음).
public sealed class VaultRepository : INoteRepository, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly VaultStore _store;
    private readonly Dictionary<Guid, Note> _notes = new();
    private bool _disposed;

    public VaultRepository(string folder)
    {
        _store = new VaultStore(folder);
        foreach (var n in _store.Load()) _notes[n.Id] = n;
    }

    /// 폴더 외부 변경을 반영해 캐시를 다시 읽는다(파일 감시자에서 호출).
    public async Task ReloadAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _notes.Clear();
            foreach (var n in _store.Load()) _notes[n.Id] = n;
        }
        finally { _gate.Release(); }
    }

    /// 감시자의 에코 판정 — 최근(3초) 우리가 쓴 파일이면 true.
    public bool IsRecentSelfWrite(string fileName) => _store.IsRecentSelfWrite(fileName);

    /// 폴더를 다시 읽어 캐시를 교체하고 이전 캐시와의 차이를 돌려준다(볼트 감시자용).
    /// 스냅샷·교체·diff 계산이 저장 경로(_gate)와 같은 임계구역에서 원자적으로 일어나므로
    /// 저장 유실·유령 diff 가 없다. ModifiedAt 은 우리 쓰기마다 갱신되는 소음이라 판정에 안 쓴다.
    public async Task<VaultDiff> ReloadWithDiffAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var old = _notes.Values.ToDictionary(
                n => n.Id, n => (n.Content, n.Title, n.Format, n.PlainText));

            _notes.Clear();
            foreach (var n in _store.Load()) _notes[n.Id] = n;

            var added = new List<Note>();
            var changed = new List<(Note Fresh, string OldContent)>();
            foreach (var n in _notes.Values)
            {
                if (!old.TryGetValue(n.Id, out var o)) { added.Add(n); continue; }
                // 파일 정규형(볼트 직렬화가 실제로 쓰는 형태)끼리 비교해야 유령 변경이 없다:
                // 리치(XAML) 노트는 파일에 PlainText 로 강등 저장되고, 본문 끝 공백은 잘려 나간다.
                var (oc, of) = CanonOf(o.Content, o.Format, o.PlainText);
                var (nc, nf) = CanonOf(n.Content, n.Format, n.PlainText);
                if (!string.Equals(oc, nc, StringComparison.Ordinal)
                    || !string.Equals(o.Title, n.Title, StringComparison.Ordinal)
                    || of != nf)
                {
                    changed.Add((n, o.Content));   // UI 판정용 OldContent 는 원본 그대로 전달
                }
            }
            var removed = old
                .Where(kv => !_notes.ContainsKey(kv.Key))
                .Select(kv => (kv.Key, kv.Value.Content))
                .ToList();

            return new VaultDiff(added, removed, changed);
        }
        finally { _gate.Release(); }
    }

    /// 볼트 파일 정규형: 리치(XAML)는 PlainText 로 강등 저장되고 본문 끝 공백·CRLF 은 정규화된다
    /// (<see cref="Utils.VaultMarkdown"/> 왕복 규칙과 동일). diff 는 이 형태끼리 비교해야
    /// 자기 저장 직후 리로드가 유령 변경을 만들지 않는다.
    private static (string Content, NoteContentFormat Format) CanonOf(
        string? content, NoteContentFormat format, string? plainText)
    {
        var (c, f) = format == NoteContentFormat.RichTextXaml
            ? (plainText ?? string.Empty, NoteContentFormat.PlainText)
            : (content ?? string.Empty, format);
        return (c.Replace("\r\n", "\n").TrimEnd(), f);
    }

    public Task<IReadOnlyList<Note>> GetAllAsync() => Read(() =>
        (IReadOnlyList<Note>)_notes.Values.Where(n => !n.IsDeleted)
            .OrderByDescending(n => n.ModifiedAt).ToList());

    public Task<IReadOnlyList<Note>> GetTrashedAsync() => Read(() =>
        (IReadOnlyList<Note>)_notes.Values.Where(n => n.IsDeleted)
            .OrderByDescending(n => n.DeletedAt ?? n.ModifiedAt).ToList());

    public Task<Note?> GetByIdAsync(Guid id) => Read(() =>
        _notes.TryGetValue(id, out var n) && !n.IsDeleted ? n : null);

    public Task<Note?> FindByTitleAsync(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return Task.FromResult<Note?>(null);
        var t = title.Trim();
        return Read(() => _notes.Values.FirstOrDefault(n =>
            !n.IsDeleted && string.Equals(n.Title, t, StringComparison.OrdinalIgnoreCase)));
    }

    public Task<Note?> FindByLinkedPathAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return Task.FromResult<Note?>(null);
        var target = path.Trim();
        return Read(() => _notes.Values.FirstOrDefault(n =>
            !n.IsDeleted && n.LinkedFilePath != null &&
            string.Equals(n.LinkedFilePath, target, StringComparison.OrdinalIgnoreCase)));
    }

    public Task UpsertAsync(Note note) => Write(() =>
    {
        note.ModifiedAt = DateTime.UtcNow;
        _notes[note.Id] = note;
        _store.SaveOne(note);
    });

    public Task SaveContentAsync(Note note) => Write(() =>
    {
        // 콘텐츠 저장 전용 — 사라진 노트는 되살리지 않고, 휴지통 상태는 캐시 값을 보존한다.
        if (!_notes.TryGetValue(note.Id, out var existing)) return;
        note.IsDeleted = existing.IsDeleted;
        note.DeletedAt = existing.DeletedAt;
        note.ModifiedAt = DateTime.UtcNow;
        _notes[note.Id] = note;
        _store.SaveOne(note);
    });

    public Task DeleteAsync(Guid id) => Write(() =>
    {
        if (!_notes.TryGetValue(id, out var n)) return;
        n.IsDeleted = true;
        n.DeletedAt = DateTime.UtcNow;
        _store.SaveOne(n);
    });

    public Task RestoreAsync(Guid id) => Write(() =>
    {
        if (!_notes.TryGetValue(id, out var n)) return;
        n.IsDeleted = false;
        n.DeletedAt = null;
        n.ModifiedAt = DateTime.UtcNow;
        _store.SaveOne(n);
    });

    public Task PurgeAsync(Guid id) => Write(() =>
    {
        if (_notes.Remove(id)) _store.DeleteFile(id);
    });

    public Task<int> PurgeTrashedOlderThanAsync(DateTime cutoff) => Write(() =>
    {
        var stale = _notes.Values
            .Where(n => n.IsDeleted && n.DeletedAt != null && n.DeletedAt < cutoff)
            .Select(n => n.Id).ToList();
        foreach (var id in stale) { _notes.Remove(id); _store.DeleteFile(id); }
        return stale.Count;
    });

    private async Task<T> Read<T>(Func<T> body)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try { return body(); } finally { _gate.Release(); }
    }

    private async Task Write(Action body)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try { body(); } finally { _gate.Release(); }
    }

    private async Task<T> Write<T>(Func<T> body)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try { return body(); } finally { _gate.Release(); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _gate.Dispose();
    }
}
