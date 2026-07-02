using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StickyPad.Services;
using StickyPad.Utils;

namespace StickyPad.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly IHotkeyService _hotkeys;
    private readonly IAutoStartService _autoStart;

    [ObservableProperty]
    private bool _autoStartWithWindows;

    [ObservableProperty]
    private bool _globalHotkeysEnabled;

    [ObservableProperty]
    private string _newNoteHotkey;

    [ObservableProperty]
    private string _notesListHotkey;

    [ObservableProperty]
    private bool _autoCheckForUpdates;

    [ObservableProperty]
    private string? _validationError;

    /// Raised after a successful save so the hosting window can close itself.
    public event EventHandler? Saved;

    public SettingsViewModel(ISettingsService settings, IHotkeyService hotkeys, IAutoStartService autoStart)
    {
        _settings = settings;
        _hotkeys = hotkeys;
        _autoStart = autoStart;

        var current = settings.Current;
        // Auto-start's source of truth is the registry, not the settings file.
        _autoStartWithWindows = autoStart.IsEnabled;
        _globalHotkeysEnabled = current.GlobalHotkeysEnabled;
        _newNoteHotkey = current.NewNoteHotkey;
        _notesListHotkey = current.NotesListHotkey;
        _autoCheckForUpdates = current.AutoCheckForUpdates;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (GlobalHotkeysEnabled)
        {
            if (!HotkeyGesture.TryParse(NewNoteHotkey, out _, out _))
            {
                ValidationError = "새 노트 단축키가 올바르지 않습니다 (수정키 + 키 조합 필요).";
                return;
            }
            if (!HotkeyGesture.TryParse(NotesListHotkey, out _, out _))
            {
                ValidationError = "전체 목록 단축키가 올바르지 않습니다 (수정키 + 키 조합 필요).";
                return;
            }
        }
        ValidationError = null;

        var current = _settings.Current;
        current.GlobalHotkeysEnabled = GlobalHotkeysEnabled;
        current.NewNoteHotkey = NewNoteHotkey;
        current.NotesListHotkey = NotesListHotkey;
        current.AutoStartWithWindows = AutoStartWithWindows;
        current.AutoCheckForUpdates = AutoCheckForUpdates;
        await _settings.SaveAsync().ConfigureAwait(true);

        _autoStart.SetEnabled(AutoStartWithWindows);
        _hotkeys.Apply(GlobalHotkeysEnabled, NewNoteHotkey, NotesListHotkey);

        Saved?.Invoke(this, EventArgs.Empty);
    }
}
