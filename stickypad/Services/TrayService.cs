using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using H.NotifyIcon;
using Microsoft.Extensions.Logging;
using StickyPad.Models;
using StickyPad.Resources;
using StickyPad.Utils;

namespace StickyPad.Services;

public sealed class TrayService : ITrayService
{
    private readonly IWindowManager _windowManager;
    private readonly IBackupService _backupService;
    private readonly IAutoStartService _autoStartService;
    private readonly IUpdateService _updateService;
    private readonly ILogger<TrayService> _logger;

    private TaskbarIcon? _icon;
    private MenuItem? _autoStartMenu;

    public TrayService(
        IWindowManager windowManager,
        IBackupService backupService,
        IAutoStartService autoStartService,
        IUpdateService updateService,
        ILogger<TrayService> logger)
    {
        _windowManager = windowManager;
        _backupService = backupService;
        _autoStartService = autoStartService;
        _updateService = updateService;
        _logger = logger;
    }

    public void Initialize()
    {
        try
        {
            _icon = new TaskbarIcon
            {
                // ImageSource 비동기 변환 경로(OnIconSourceChanged → ToIconAsync)는
                // 환경에 따라 디스패처에 unhandled 예외를 흘려 트레이가 빈 채로 남는
                // 사례가 있어, System.Drawing.Icon 을 직접 할당하는 동기 경로를 사용한다.
                Icon = IconFactory.CreateTrayIcon(16),
                ToolTipText = Strings.Tray_Tooltip,
                NoLeftClickDelay = true,
            };
            _icon.LeftClickCommand = new DelegateCommand(_ => _windowManager.ToggleAllVisible());
            _icon.DoubleClickCommand = new DelegateCommand(_ => _windowManager.CreateAndShowNew());
            _icon.ContextMenu = BuildContextMenu();

            // TaskbarIcon 은 visual tree 에 attach 되어 있을 때만 Loaded 이벤트가 발생해
            // 내부 Shell_NotifyIcon 호출이 실행된다. 코드비하인드에서 new 만 하면 트레이에
            // 절대 등장하지 않으므로 명시적으로 ForceCreate() 를 호출한다.
            _icon.ForceCreate(enablesEfficiencyMode: false);
            _logger.LogInformation("Tray icon created");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize tray icon");
        }
    }

    private ContextMenu BuildContextMenu()
    {
        var menu = new ContextMenu();

        menu.Items.Add(MenuItem(Strings.Tray_NewNote, () => _windowManager.CreateAndShowNew()));
        menu.Items.Add(BuildTemplateMenu());
        menu.Items.Add(MenuItem(Strings.Tray_OpenMarkdownFile, OpenMarkdownFile));
        menu.Items.Add(MenuItem(Strings.Tray_AllNotes, () => _windowManager.OpenNotesList()));
        menu.Items.Add(MenuItem(Strings.Tray_Settings, () => _windowManager.OpenSettings()));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItem(Strings.Tray_ShowAll, () => _windowManager.ShowAll()));
        menu.Items.Add(MenuItem(Strings.Tray_HideAll, () => _windowManager.HideAll()));
        menu.Items.Add(new Separator());

        _autoStartMenu = new MenuItem
        {
            Header = Strings.Settings_AutoStart,
            IsCheckable = true,
            IsChecked = _autoStartService.IsEnabled,
        };
        _autoStartMenu.Click += (_, _) => _autoStartService.SetEnabled(_autoStartMenu.IsChecked);
        menu.Items.Add(_autoStartMenu);

        menu.Items.Add(MenuItem(Strings.Tray_ExportBackup, async () =>
        {
            try { await _backupService.ExportInteractiveAsync(); }
            catch (Exception ex) { _logger.LogError(ex, "Export failed"); }
        }));
        menu.Items.Add(MenuItem(Strings.Tray_ImportBackup, async () =>
        {
            try { await _backupService.ImportInteractiveAsync(); }
            catch (Exception ex) { _logger.LogError(ex, "Import failed"); }
        }));
        menu.Items.Add(MenuItem(Strings.Tray_ExportNotesAsText, async () =>
        {
            try { await _backupService.ExportNotesAsTextAsync(); }
            catch (Exception ex) { _logger.LogError(ex, "Text export failed"); }
        }));
        menu.Items.Add(BuildVaultMenu());

        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItem(Strings.Tray_CheckForUpdates, async () =>
        {
            try { await _updateService.CheckAsync(userInitiated: true); }
            catch (Exception ex) { _logger.LogError(ex, "Update check failed"); }
        }));
        menu.Items.Add(MenuItem(Strings.Tray_Exit, () => Application.Current.Shutdown()));

        menu.Opened += (_, _) =>
        {
            if (_autoStartMenu is not null)
            {
                _autoStartMenu.IsChecked = _autoStartService.IsEnabled;
            }
        };

        return menu;
    }

    private async void OpenMarkdownFile()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = Utils.LinkedFile.OpenDialogFilter,
            Title = Strings.Tray_OpenMarkdownDialogTitle,
        };
        if (DialogOwner.Show(dlg) != true) return;
        try { await _windowManager.OpenFileAsync(dlg.FileName); }
        catch (Exception ex) { _logger.LogError(ex, "Open markdown file failed"); }
    }

    private MenuItem BuildTemplateMenu()
    {
        var parent = new MenuItem { Header = Strings.Tray_NewFromTemplate };
        foreach (var template in NoteTemplates.All)
        {
            parent.Items.Add(MenuItem(template.Name(), () => _windowManager.CreateAndShowNew(template)));
        }
        return parent;
    }

    private MenuItem BuildVaultMenu()
    {
        var parent = new MenuItem { Header = Strings.Vault_Menu };
        parent.Items.Add(MenuItem(Strings.Vault_Export, async () =>
        {
            try { await _backupService.ExportVaultAsync(); }
            catch (Exception ex) { _logger.LogError(ex, "Vault export failed"); }
        }));
        parent.Items.Add(MenuItem(Strings.Vault_Import, async () =>
        {
            try { await _backupService.ImportVaultAsync(); }
            catch (Exception ex) { _logger.LogError(ex, "Vault import failed"); }
        }));
        return parent;
    }

    private static MenuItem MenuItem(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    public void Dispose()
    {
        _icon?.Dispose();
        _icon = null;
    }

    private sealed class DelegateCommand : ICommand
    {
        private readonly Action<object?> _execute;
        public DelegateCommand(Action<object?> execute) => _execute = execute;
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => _execute(parameter);
        public event EventHandler? CanExecuteChanged { add { } remove { } }
    }
}
