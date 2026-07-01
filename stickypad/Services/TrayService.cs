using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using H.NotifyIcon;
using Microsoft.Extensions.Logging;
using StickyPad.Utils;

namespace StickyPad.Services;

public sealed class TrayService : ITrayService
{
    private readonly IWindowManager _windowManager;
    private readonly IBackupService _backupService;
    private readonly IAutoStartService _autoStartService;
    private readonly ILogger<TrayService> _logger;

    private TaskbarIcon? _icon;
    private MenuItem? _autoStartMenu;

    public TrayService(
        IWindowManager windowManager,
        IBackupService backupService,
        IAutoStartService autoStartService,
        ILogger<TrayService> logger)
    {
        _windowManager = windowManager;
        _backupService = backupService;
        _autoStartService = autoStartService;
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
                ToolTipText = "StickyPad",
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

        menu.Items.Add(MenuItem("New note  (Ctrl+Shift+N)", () => _windowManager.CreateAndShowNew()));
        menu.Items.Add(MenuItem("All notes…  (Ctrl+Shift+L)", () => _windowManager.OpenNotesList()));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItem("Show all", () => _windowManager.ShowAll()));
        menu.Items.Add(MenuItem("Hide all", () => _windowManager.HideAll()));
        menu.Items.Add(new Separator());

        _autoStartMenu = new MenuItem
        {
            Header = "Start with Windows",
            IsCheckable = true,
            IsChecked = _autoStartService.IsEnabled,
        };
        _autoStartMenu.Click += (_, _) => _autoStartService.SetEnabled(_autoStartMenu.IsChecked);
        menu.Items.Add(_autoStartMenu);

        menu.Items.Add(MenuItem("Export backup…", async () =>
        {
            try { await _backupService.ExportInteractiveAsync(); }
            catch (Exception ex) { _logger.LogError(ex, "Export failed"); }
        }));
        menu.Items.Add(MenuItem("Import backup…", async () =>
        {
            try { await _backupService.ImportInteractiveAsync(); }
            catch (Exception ex) { _logger.LogError(ex, "Import failed"); }
        }));

        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItem("Exit", () => Application.Current.Shutdown()));

        menu.Opened += (_, _) =>
        {
            if (_autoStartMenu is not null)
            {
                _autoStartMenu.IsChecked = _autoStartService.IsEnabled;
            }
        };

        return menu;
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
