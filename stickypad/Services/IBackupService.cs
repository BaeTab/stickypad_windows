using System;
using System.Collections.Generic;
using System.Threading.Tasks;

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
}
