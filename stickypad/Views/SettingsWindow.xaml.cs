using System;
using System.Windows;
using System.Windows.Input;
using StickyPad.Utils;
using StickyPad.ViewModels;

namespace StickyPad.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;

    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        Icon = IconFactory.CreateAppIcon();
        _viewModel = viewModel;
        DataContext = viewModel;
        _viewModel.Saved += OnSaved;
        Closed += (_, _) => _viewModel.Saved -= OnSaved;
    }

    private void OnSaved(object? sender, EventArgs e) => Close();

    private void Cancel_OnClick(object sender, RoutedEventArgs e) => Close();

    private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        // Ignore a lone modifier — wait until the user adds an actual key.
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
                or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
        {
            return;
        }

        var mods = Keyboard.Modifiers;
        if (mods == ModifierKeys.None) return; // a global hotkey needs at least one modifier

        var gesture = HotkeyGesture.Format(key, mods);
        if (ReferenceEquals(sender, NewNoteBox)) _viewModel.NewNoteHotkey = gesture;
        else if (ReferenceEquals(sender, NotesListBox)) _viewModel.NotesListHotkey = gesture;
    }
}
