using System;

namespace StickyPad.Services;

public interface IHotkeyService
{
    void Register(Action newNoteHandler, Action openNotesListHandler);
    void Unregister();
}
