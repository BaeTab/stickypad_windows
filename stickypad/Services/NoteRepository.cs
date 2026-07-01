using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LiteDB;
using StickyPad.Models;

namespace StickyPad.Services;

public sealed class NoteRepository : INoteRepository, IDisposable
{
    private const string CollectionName = "notes";

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly LiteDatabase _db;
    private bool _disposed;

    public NoteRepository(string dbPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        _db = new LiteDatabase($"Filename={dbPath};Connection=shared");
        var col = _db.GetCollection<Note>(CollectionName);
        col.EnsureIndex(x => x.ModifiedAt);
        col.EnsureIndex(x => x.Title);
        col.EnsureIndex(x => x.IsDeleted);
    }

    public async Task<IReadOnlyList<Note>> GetAllAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var col = _db.GetCollection<Note>(CollectionName);
            return col.Find(n => !n.IsDeleted)
                .OrderByDescending(n => n.ModifiedAt)
                .ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<Note>> GetTrashedAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var col = _db.GetCollection<Note>(CollectionName);
            return col.Find(n => n.IsDeleted)
                .OrderByDescending(n => n.DeletedAt ?? n.ModifiedAt)
                .ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<Note?> GetByIdAsync(Guid id)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var note = _db.GetCollection<Note>(CollectionName).FindById(id);
            // 휴지통 항목은 외부에서 보이지 않도록 차단. 명시적 복구는 RestoreAsync 가 담당.
            return note is null || note.IsDeleted ? null : note;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<Note?> FindByTitleAsync(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var col = _db.GetCollection<Note>(CollectionName);
            return col.Find(n => !n.IsDeleted)
                .FirstOrDefault(n => string.Equals(n.Title, title.Trim(), StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpsertAsync(Note note)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            note.ModifiedAt = DateTime.UtcNow;
            _db.GetCollection<Note>(CollectionName).Upsert(note);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveContentAsync(Note note)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var col = _db.GetCollection<Note>(CollectionName);
            // 휴지통 상태(IsDeleted/DeletedAt) 전환은 DeleteAsync/RestoreAsync/PurgeAsync 만 소유한다.
            // 콘텐츠 저장이 그 상태를 건드리면, 삭제 직후 열려 있던 노트 창이 닫히며 옛 메모리
            // 복사본(IsDeleted=false)을 flush 해 방금 삭제한 노트를 부활시킨다.
            var existing = col.FindById(note.Id);
            if (existing is null)
            {
                // 노트가 이미 영구 삭제됨(목록에서 삭제 → 빈 노트 PurgeAsync, 또는 휴지통 영구삭제).
                // 창이 닫히는 도중 뒤늦게 흘러든 디바운스 저장이 stale 복사본(IsDeleted=false)을
                // Upsert 로 재삽입하면 방금 지운 노트가 활성 목록에 부활한다. 콘텐츠 저장은 '기존
                // 노트 갱신' 전용이므로, 사라진 노트는 다시 만들지 않고 그대로 무시한다.
                return;
            }
            // DB 에 저장된 휴지통 상태를 그대로 보존해 in-memory 복사본의 stale 값이 삭제를 되돌리지 못하게 한다.
            note.IsDeleted = existing.IsDeleted;
            note.DeletedAt = existing.DeletedAt;
            note.ModifiedAt = DateTime.UtcNow;
            col.Update(note);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteAsync(Guid id)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var col = _db.GetCollection<Note>(CollectionName);
            var note = col.FindById(id);
            if (note is null) return;
            note.IsDeleted = true;
            note.DeletedAt = DateTime.UtcNow;
            col.Update(note);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RestoreAsync(Guid id)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var col = _db.GetCollection<Note>(CollectionName);
            var note = col.FindById(id);
            if (note is null) return;
            note.IsDeleted = false;
            note.DeletedAt = null;
            note.ModifiedAt = DateTime.UtcNow;
            col.Update(note);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task PurgeAsync(Guid id)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _db.GetCollection<Note>(CollectionName).Delete(id);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<int> PurgeTrashedOlderThanAsync(DateTime cutoff)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var col = _db.GetCollection<Note>(CollectionName);
            var stale = col.Find(n => n.IsDeleted && n.DeletedAt != null && n.DeletedAt < cutoff)
                .Select(n => n.Id)
                .ToList();
            foreach (var id in stale) col.Delete(id);
            return stale.Count;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _db.Dispose();
        _gate.Dispose();
    }
}
