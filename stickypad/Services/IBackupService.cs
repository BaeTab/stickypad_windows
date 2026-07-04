using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using StickyPad.Models;

namespace StickyPad.Services;

public interface IBackupService
{
    Task ExportInteractiveAsync();
    Task ImportInteractiveAsync();

    /// Exports active notes to a human-readable Markdown/text file (not a restorable backup).
    Task ExportNotesAsTextAsync();

    /// 지정한 노트들을 선택한 형식(노트별 .md 파일 / 단일 HTML / PDF)으로 내보낸다.
    /// noteIds 순서를 그대로 유지하며, 활성(휴지통 아님) 노트만 대상으로 한다.
    Task ExportNotesAsync(IReadOnlyList<Guid> noteIds, ExportFormat format);

    /// 단일 노트(현재 창)를 선택한 형식으로 저장. Markdown 은 파일 1개(SaveFileDialog).
    Task ExportSingleNoteAsync(Note note, ExportFormat format);

    /// 단일 노트를 시스템 인쇄 UI로 인쇄(오프스크린이 아닌, 보이는 미리보기 창).
    Task PrintNoteAsync(Note note);
}
