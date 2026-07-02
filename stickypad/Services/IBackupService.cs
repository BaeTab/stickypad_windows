using System.Threading.Tasks;

namespace StickyPad.Services;

public interface IBackupService
{
    Task ExportInteractiveAsync();
    Task ImportInteractiveAsync();

    /// Exports active notes to a human-readable Markdown/text file (not a restorable backup).
    Task ExportNotesAsTextAsync();
}
