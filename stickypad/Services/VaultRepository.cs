using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StickyPad.Models;

namespace StickyPad.Services;

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
