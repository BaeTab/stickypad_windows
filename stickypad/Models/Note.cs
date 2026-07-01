using System;
using System.Collections.Generic;

namespace StickyPad.Models;

public sealed class Note
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Content { get; set; } = string.Empty;
    public NoteContentFormat Format { get; set; } = NoteContentFormat.PlainText;

    /// Plain-text projection of Content. Cached so the notes-list and search don't have to re-parse XAML.
    public string PlainText { get; set; } = string.Empty;

    /// Auto-derived from the first non-empty line of PlainText. Used by `[[Title]]` link resolution.
    public string Title { get; set; } = string.Empty;

    public List<string> Tags { get; set; } = new();

    public NoteColor Color { get; set; } = NoteColor.Yellow;

    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 280;
    public double Height { get; set; } = 280;

    public double Opacity { get; set; } = 1.0;
    public bool IsAlwaysOnTop { get; set; }
    public bool IsHidden { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;

    /// 휴지통 표식. true 면 일반 조회·검색·창 복원 대상에서 제외되고
    /// NotesListWindow 의 휴지통 탭에서만 보인다.
    public bool IsDeleted { get; set; }

    /// 휴지통으로 옮긴 시각. 30 일 경과 시 자동 영구 삭제 기준.
    public DateTime? DeletedAt { get; set; }
}
