using System;
using System.Collections.Concurrent;
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

    /// 자기쓰기 원장의 에코 판정 시간창 — 이 안에 온 FSW 이벤트는 우리가 쓴 것으로 본다.
    private const int SelfWriteWindowSeconds = 3;

    private readonly string _folder;
    private readonly Dictionary<Guid, string> _files = new(); // id -> 파일명(폴더 기준)

    /// 자기쓰기 원장(파일명 → 기록 시각 UTC). 볼트 감시자의 에코 억제용 — IO 절약 최적화일 뿐,
    /// 판정이 틀려도 diff 무결성이 오동작을 막는다(spec-5 §3.4).
    private readonly ConcurrentDictionary<string, DateTime> _selfWrites = new(StringComparer.OrdinalIgnoreCase);

    public VaultStore(string folder) => _folder = folder;

    private void RecordSelfWrite(string fileName) => _selfWrites[fileName] = DateTime.UtcNow;

    /// 최근(3초) 자기쓰기 파일이면 true. 시간창을 지난 항목은 게으르게 청소한다.
    public bool IsRecentSelfWrite(string fileName) => IsRecentSelfWrite(fileName, DateTime.UtcNow);

    internal bool IsRecentSelfWrite(string fileName, DateTime nowUtc)
    {
        if (_selfWrites.TryGetValue(fileName, out var at))
        {
            if ((nowUtc - at).TotalSeconds <= SelfWriteWindowSeconds) return true;
            _selfWrites.TryRemove(fileName, out _);
        }
        return false;
    }

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
            bool hadId;
            try { note = VaultMarkdown.FromMarkdown(File.ReadAllText(file), Path.GetFileNameWithoutExtension(file), out hadId); }
            catch { continue; }

            var name = Path.GetFileName(file);

            // 프런트매터 id 가 없는 손수 추가 .md — 파일명 기반의 안정적 id 를 부여한다.
            // 랜덤 id 면 리로드마다 다른 노트로 보여 감시 diff 가 진동(알림 스팸·숨은 창 누적)하고
            // 사이드카 상태(위치 등)도 재시작마다 유실된다. 편집하는 순간 이 id 가 파일에 영속된다.
            if (!hadId) note.Id = DisplacedId(Guid.Empty, name);

            // id 중복(충돌 사본의 전형) — 정본이 아닌 쪽이 원본을 캐시에서 가리고, 이후 저장이
            // 충돌 사본 파일에 기록되는 데이터 사고를 막는다(spec-5 §3.6). 읽기 전용 패스 —
            // 여기서 파일을 다시 쓰지 않는다. 밀려난 쪽은 새 Guid 로 별도 노트가 되어 목록에서
            // 사용자가 확인·정리할 수 있다(그 노트를 편집하는 순간 새 id 가 파일에 영속된다).
            if (_files.TryGetValue(note.Id, out var existingName))
            {
                var indexFile = index.TryGetValue(note.Id.ToString(), out var idxState) ? idxState.File : null;
                bool newIsCanonical;
                if (indexFile is not null && string.Equals(name, indexFile, StringComparison.OrdinalIgnoreCase))
                    newIsCanonical = true;                                   // 인덱스가 새 파일을 가리킴
                else if (indexFile is not null && string.Equals(existingName, indexFile, StringComparison.OrdinalIgnoreCase))
                    newIsCanonical = false;                                  // 인덱스가 기존 파일을 가리킴
                else
                {
                    // 인덱스로 판정 불가 → 충돌 사본 패턴이 아닌 쪽을 정본으로. 그래도 모호하면 먼저 읽힌 쪽.
                    bool Sibling(string f) => File.Exists(Path.Combine(_folder, f));
                    var existingIsConflict = ConflictCopyDetector.IsConflictCopy(existingName, Sibling);
                    var newIsConflict = ConflictCopyDetector.IsConflictCopy(name, Sibling);
                    newIsCanonical = existingIsConflict && !newIsConflict;
                }

                if (newIsCanonical)
                {
                    // 먼저 읽힌 쪽이 밀려난다 — 결정적 파생 id 로 별도 노트 유지(리로드마다 같은 id →
                    // 감시 diff 가 유령 추가/삭제로 진동하지 않는다). 파일은 다시 쓰지 않는다.
                    var displaced = result.First(n => n.Id == note.Id);
                    displaced.Id = DisplacedId(note.Id, existingName);
                    _files[displaced.Id] = existingName;
                    _files[note.Id] = name;
                    if (index.TryGetValue(note.Id.ToString(), out var s2)) Apply(note, s2);
                    result.Add(note);
                }
                else
                {
                    // 새로 읽힌 쪽이 밀려난다.
                    note.Id = DisplacedId(note.Id, name);
                    _files[note.Id] = name;
                    result.Add(note);   // 사이드카 상태는 정본 소유 — 적용하지 않음
                }
                continue;
            }

            _files[note.Id] = name; // 실제 파일명 기억 → 이후 저장 시 재사용
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
            RecordSelfWrite(name);
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
        RecordSelfWrite(name);
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
            RecordSelfWrite(name);
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

    /// 밀려난(id 중복) 파일의 인메모리 id — (원본 id, 파일명)에서 결정적으로 파생해
    /// 리로드가 반복돼도 같은 id 가 나온다. 사용자가 그 노트를 편집하면 이 id 가 파일에 영속된다.
    private static Guid DisplacedId(Guid originalId, string fileName)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(originalId.ToString("N") + "|" + fileName.ToLowerInvariant()));
        return new Guid(bytes.AsSpan(0, 16));
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
