using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using StickyPad.Models;
using StickyPad.Utils;

namespace StickyPad.Services;

/// 볼트 폴더 ↔ 활성 노트 집합의 저장 엔진(2.0 라이브 볼트 모드 기반).
///
/// 각 노트의 내용은 사람이 읽는 `.md`(<see cref="VaultMarkdown"/>)로 저장하고, StickyPad 고유의
/// 표시 상태(창 위치·크기·투명도·항상 위·숨김·연동 경로)는 폴더 안 사이드카
/// <c>.stickypad-index.json</c> 에 담는다 — `.md` 파일을 Obsidian 등에서도 깔끔하게 쓸 수 있게.
///
/// 휴지통 노트는 볼트에 쓰지 않는다(볼트 = 활성 노트). 폴더에 손수 넣은 프런트매터 없는
/// `.md` 도 새 노트로 읽어들인다.
public static class VaultStore
{
    private const string IndexFileName = ".stickypad-index.json";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// 창 위치·크기 등 `.md` 로는 표현하지 않는 앱 표시 상태.
    private sealed record NoteState(
        string File, double X, double Y, double Width, double Height,
        double Opacity, bool IsAlwaysOnTop, bool IsHidden, string? LinkedFilePath);

    public static void SaveAll(string folder, IReadOnlyList<Note> notes)
    {
        Directory.CreateDirectory(folder);
        var prior = ReadIndex(folder); // id -> 이전 상태(파일명 안정화에 재사용)

        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in EnumerateMarkdown(folder)) taken.Add(Path.GetFileName(f).ToLowerInvariant());

        var index = new Dictionary<string, NoteState>();
        foreach (var note in notes)
        {
            var key = note.Id.ToString();
            // 같은 id 는 기존 파일명을 재사용(제목이 바뀌어도 파일이 튀지 않게).
            string fileName;
            if (prior.TryGetValue(key, out var old) && !string.IsNullOrEmpty(old.File))
            {
                fileName = old.File;
            }
            else
            {
                fileName = ExportNaming.UniqueFileName(ExportNaming.SafeFileName(note.Title), ".md", taken);
            }

            File.WriteAllText(Path.Combine(folder, fileName), VaultMarkdown.ToMarkdown(note));
            index[key] = new NoteState(fileName, note.X, note.Y, note.Width, note.Height,
                note.Opacity, note.IsAlwaysOnTop, note.IsHidden, note.LinkedFilePath);
        }

        File.WriteAllText(Path.Combine(folder, IndexFileName), JsonSerializer.Serialize(index, JsonOptions));
    }

    public static IReadOnlyList<Note> LoadAll(string folder)
    {
        var result = new List<Note>();
        if (!Directory.Exists(folder)) return result;

        var index = ReadIndex(folder);
        foreach (var file in EnumerateMarkdown(folder))
        {
            Note note;
            try { note = VaultMarkdown.FromMarkdown(File.ReadAllText(file), Path.GetFileNameWithoutExtension(file)); }
            catch { continue; }

            // 사이드카에 표시 상태가 있으면 적용, 없으면 Note 기본값 유지(손수 추가한 .md).
            if (index.TryGetValue(note.Id.ToString(), out var s))
            {
                note.X = s.X; note.Y = s.Y; note.Width = s.Width; note.Height = s.Height;
                note.Opacity = s.Opacity; note.IsAlwaysOnTop = s.IsAlwaysOnTop;
                note.IsHidden = s.IsHidden; note.LinkedFilePath = s.LinkedFilePath;
            }
            result.Add(note);
        }
        return result;
    }

    private static IEnumerable<string> EnumerateMarkdown(string folder) =>
        Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder, "*.md", SearchOption.TopDirectoryOnly)
            : Enumerable.Empty<string>();

    private static Dictionary<string, NoteState> ReadIndex(string folder)
    {
        var path = Path.Combine(folder, IndexFileName);
        if (!File.Exists(path)) return new();
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, NoteState>>(File.ReadAllText(path)) ?? new();
        }
        catch
        {
            return new(); // 손상된 인덱스는 무시 — .md 내용은 그대로 읽는다.
        }
    }
}
