using System.Threading.Tasks;

namespace StickyPad.Services;

public interface IBackupService
{
    Task ExportInteractiveAsync();
    Task ImportInteractiveAsync();
}
