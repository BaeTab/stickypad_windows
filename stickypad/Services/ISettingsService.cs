using System.Threading.Tasks;
using StickyPad.Models;

namespace StickyPad.Services;

public interface ISettingsService
{
    AppSettings Current { get; }
    Task SaveAsync();
}
