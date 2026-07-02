using System.Threading.Tasks;

namespace StickyPad.Services;

public interface IUpdateService
{
    /// Checks GitHub Releases for a newer version and, if the user confirms, downloads it
    /// and self-replaces on restart. When <paramref name="userInitiated"/> is true it also
    /// reports "up to date" / failures; otherwise it stays silent unless an update is found.
    Task CheckAsync(bool userInitiated);
}
