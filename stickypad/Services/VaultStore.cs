using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using StickyPad.Models;
using StickyPad.Utils;

namespace StickyPad.Services;

/// 볼트 폴더 ↔ 노트 집합(활성+휴지통)의 저장 엔진(2.0 라이브 볼트 모드 기반).
///
/// 각 노트의 내용은 사람이 읽는 `.md`(<see cref="VaultMarkdown"/>)로, StickyPad 고유의 표시/상태
/// (창 위치·크기·투명도·항상 위·숨김·연동 경로·휴지통 여부)는 폴더 안 사이드카
/// <c>.stickypad-index.json</c> 에 담는다 — `.md` 를 Obsidian 등에서도 깔끔하게 쓸 수 있게.
///
/// 인스턴스가 id→파일명 맵을 보유해 파일명을 안정적으로 유지한다(제목이 바뀌어도 같은 파일).
/// 파일을 뭉텅이로 지우지 않는다 — 삭제는 <see cref="DeleteFile"/> 로 특정 노트 하나만
/// (사용자가 손수 넣은 다른 `.md` 를 실수로 지우지 않도록).
public sealed class VaultStore
{
    private const string IndexFileName = ".stickypad-index.json";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _folder;
    private readonly Dictionary<Guid, string> _files = new(); // id -> 파일명(폴더 기준)

    public VaultStore(string folder) => _folder = folder;

    private sealed record NoteState(
        string File, double X, double Y, double Width, double Height,
        double Opacity, bool IsAlwaysOnTop, bool IsHidden, string? LinkedFilePath,
        bool IsDeleted, DateTime? DeletedAt);

    public IReadOnlyList<Note> Load()
    {
        _files.Clear();
        var result = new List<Note>();
        if (!Directory.Exists(_folder)) return result;

        var index = ReadIndex();
        foreach (var file in Directory.EnumerateFiles(_folder, "*.md", SearchOption.TopDirectoryOnly))
        {
            Note note;
            try { note = VaultMarkdown.FromMarkdown(File.ReadAllText(file), Path.GetFileNameWithoutExtension(file)); }
            catch { continue; }

            _files[note.Id] = Path.GetFileName(file); // 실제 파일명 기억 → 이후 저장 시 재사용
            if (index.TryGetValue(note.Id.ToString(), out var s)) Apply(note, s);
            result.Add(note);
        }
        return result;
    }

    /// 전체 노트를 저장(마이그레이션·최초 기록용).
    public void Save(IReadOnlyList<Note> notes)
    {
        Directory.CreateDirectory(_folder);
        var index = ReadIndex();
        foreach (var note in notes)
        {
            var name = EnsureFileName(note);
            File.WriteAllText(Path.Combine(_folder, name), VaultMarkdown.ToMarkdown(note));
            index[note.Id.ToString()] = StateOf(note, name);
        }
        WriteIndex(index);
    }

    /// 노트 하나만 저장(변경 시 사용 — 파일 1개 + 인덱스만 갱신).
    public void SaveOne(Note note)
    {
        Directory.CreateDirectory(_folder);
        var name = EnsureFileName(note);
        File.WriteAllText(Path.Combine(_folder, name), VaultMarkdown.ToMarkdown(note));
        var index = ReadIndex();
        index[note.Id.ToString()] = StateOf(note, name);
        WriteIndex(index);
    }

    /// 이 저장소가 관리하는 노트 하나의 .md(및 인덱스 항목)만 삭제한다. 영구 삭제(purge)용.
    public void DeleteFile(Guid id)
    {
        if (_files.TryGetValue(id, out var name))
        {
            try { File.Delete(Path.Combine(_folder, name)); } catch { /* 이미 없거나 잠김 — 무시 */ }
            _files.Remove(id);
        }
        var index = ReadIndex();
        if (index.Remove(id.ToString())) WriteIndex(index);
    }

    private string EnsureFileName(Note note)
    {
        if (_files.TryGetValue(note.Id, out var existing)) return existing;

        var taken = new HashSet<string>(_files.Values, StringComparer.OrdinalIgnoreCase);
        foreach (var f in Directory.EnumerateFiles(_folder, "*.md", SearchOption.TopDirectoryOnly))
            taken.Add(Path.GetFileName(f).ToLowerInvariant());

        var name = ExportNaming.UniqueFileName(ExportNaming.SafeFileName(note.Title), ".md", taken);
        _files[note.Id] = name;
        return name;
    }

    private static NoteState StateOf(Note note, string name) => new(
        name, note.X, note.Y, note.Width, note.Height, note.Opacity,
        note.IsAlwaysOnTop, note.IsHidden, note.LinkedFilePath, note.IsDeleted, note.DeletedAt);

    private static void Apply(Note note, NoteState s)
    {
        note.X = s.X; note.Y = s.Y; note.Width = s.Width; note.Height = s.Height;
        note.Opacity = s.Opacity; note.IsAlwaysOnTop = s.IsAlwaysOnTop;
        note.IsHidden = s.IsHidden; note.LinkedFilePath = s.LinkedFilePath;
        note.IsDeleted = s.IsDeleted; note.DeletedAt = s.DeletedAt;
    }

    private Dictionary<string, NoteState> ReadIndex()
    {
        var path = Path.Combine(_folder, IndexFileName);
        if (!File.Exists(path)) return new();
        try { return JsonSerializer.Deserialize<Dictionary<string, NoteState>>(File.ReadAllText(path)) ?? new(); }
        catch { return new(); } // 손상된 인덱스는 무시 — .md 내용은 그대로 읽는다.
    }

    private void WriteIndex(Dictionary<string, NoteState> index) =>
        File.WriteAllText(Path.Combine(_folder, IndexFileName), JsonSerializer.Serialize(index, JsonOptions));
}
