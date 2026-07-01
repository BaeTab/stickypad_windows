using System;
using System.Threading.Tasks;
using StickyPad.Views;

namespace StickyPad.Services;

public interface IWindowManager
{
    Task RestoreAllAsync();
    Task ReloadAsync();
    NoteWindow CreateAndShowNew();
    void ShowAll();
    void HideAll();
    void ToggleAllVisible();
    void OpenNotesList();
    bool FocusNoteByTitle(string title);
    bool FocusNoteById(Guid id);
}
