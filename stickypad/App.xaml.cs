using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using StickyPad.Services;
using StickyPad.Utils;
using StickyPad.ViewModels;

namespace StickyPad;

public partial class App : Application
{
    private const string SingleInstanceMutexName = @"Local\StickyPad.SingleInstance";

    private IHost? _host;
    private Mutex? _singleInstanceMutex;
    private bool _ownsMutex;
    private InstanceChannel? _instanceChannel;

    public static IServiceProvider Services =>
        ((App)Current)._host?.Services
        ?? throw new InvalidOperationException("Host not initialized.");

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error(args.Exception, "Dispatcher unhandled exception");
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Error(args.ExceptionObject as Exception, "AppDomain unhandled exception");
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error(args.Exception, "Unobserved task exception");
            args.SetObserved();
        };

        // 커맨드라인 인자로 넘어온(더블클릭/'연결 프로그램'/드롭) 파일 경로 수집.
        var startupFiles = ExtractFilePaths(e.Args);

        _singleInstanceMutex = new Mutex(initiallyOwned: false, SingleInstanceMutexName, out _);
        try
        {
            _ownsMutex = _singleInstanceMutex.WaitOne(TimeSpan.Zero, exitContext: false);
        }
        catch (AbandonedMutexException)
        {
            // Previous instance crashed without releasing — we now own the mutex, proceed.
            _ownsMutex = true;
        }

        if (!_ownsMutex)
        {
            // 이미 다른 인스턴스가 실행 중 — 열려던 파일이 있으면 그 인스턴스로 넘기고 조용히 종료.
            if (startupFiles.Length > 0) InstanceChannel.TrySend(startupFiles);
            Shutdown(0);
            return;
        }

        // .md '연결 프로그램' 목록에 자기 자신을 등록(HKCU, 조용히 best-effort).
        FileAssociationRegistrar.Ensure(Environment.ProcessPath ?? string.Empty);

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var dataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "StickyPad");
        Directory.CreateDirectory(dataRoot);

        var dbPath = Path.Combine(dataRoot, "notes.db");
        var settingsPath = Path.Combine(dataRoot, "settings.json");
        var logPath = Path.Combine(dataRoot, "logs", "app-.log");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Debug()
            .WriteTo.File(logPath, rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7)
            .CreateLogger();

        _host = Host.CreateDefaultBuilder()
            .UseSerilog()
            .ConfigureServices(services =>
            {
                services.AddSingleton<INoteRepository>(_ => new NoteRepository(dbPath));
                services.AddSingleton<ISettingsService>(sp => new SettingsService(
                    settingsPath, sp.GetRequiredService<ILogger<SettingsService>>()));
                services.AddSingleton<IAutoStartService, AutoStartService>();
                services.AddSingleton<IHotkeyService, HotkeyService>();
                services.AddSingleton<IWindowManager, WindowManager>();
                services.AddSingleton<IBackupService, BackupService>();
                services.AddSingleton<IUpdateService, UpdateService>();
                services.AddSingleton<ITrayService, TrayService>();
                services.AddTransient<NotesListViewModel>();
                services.AddTransient<SettingsViewModel>();
            })
            .Build();

        await _host.StartAsync().ConfigureAwait(true);

        try
        {
            LocalizationService.ApplyCulture(_host.Services.GetRequiredService<ISettingsService>().Current.Language);

            var repo = _host.Services.GetRequiredService<INoteRepository>();
            try
            {
                var purged = await repo
                    .PurgeTrashedOlderThanAsync(DateTime.UtcNow.AddDays(-30))
                    .ConfigureAwait(true);
                if (purged > 0) Log.Information("Purged {Count} expired trashed notes", purged);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to auto-purge expired trash");
            }

            var manager = _host.Services.GetRequiredService<IWindowManager>();
            await manager.RestoreAllAsync().ConfigureAwait(true);

            _host.Services.GetRequiredService<ITrayService>().Initialize();

            // 실행 중 다른 프로세스가 넘겨주는 파일 경로를 받아 여는 named-pipe 서버 시작.
            _instanceChannel = new InstanceChannel();
            _instanceChannel.StartServer(paths => Dispatcher.InvokeAsync(() => OpenFiles(manager, paths)));

            // 이 실행이 파일 인자와 함께 시작됐다면(더블클릭 등) 지금 연다.
            if (startupFiles.Length > 0) OpenFiles(manager, startupFiles);

            var settings = _host.Services.GetRequiredService<ISettingsService>().Current;
            var hotkeys = _host.Services.GetRequiredService<IHotkeyService>();
            hotkeys.Configure(
                newNoteHandler: () => manager.CreateAndShowNew(),
                openNotesListHandler: manager.OpenNotesList);
            hotkeys.Apply(settings.GlobalHotkeysEnabled, settings.NewNoteHotkey, settings.NotesListHotkey);

            if (settings.AutoCheckForUpdates)
            {
                var updater = _host.Services.GetRequiredService<IUpdateService>();
                _ = Task.Run(() => updater.CheckAsync(userInitiated: false));
            }
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Startup failed");
            MessageBox.Show($"StickyPad failed to start:\n{ex.Message}", "StickyPad",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private static string[] ExtractFilePaths(string[] args)
    {
        if (args is null || args.Length == 0) return Array.Empty<string>();
        var paths = new List<string>();
        foreach (var arg in args)
        {
            if (string.IsNullOrWhiteSpace(arg)) continue;
            var full = LinkedFile.Normalize(arg);
            if (File.Exists(full) && LinkedFile.IsSupported(full)) paths.Add(full);
        }
        return paths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static async void OpenFiles(IWindowManager manager, string[] paths)
    {
        foreach (var path in paths)
        {
            try { await manager.OpenFileAsync(path).ConfigureAwait(true); }
            catch (Exception ex) { Log.Error(ex, "Failed to open file {Path}", path); }
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        try
        {
            _instanceChannel?.Dispose();
            if (_host is not null)
            {
                _host.Services.GetService<IHotkeyService>()?.Unregister();
                _host.Services.GetService<ITrayService>()?.Dispose();
                if (_host.Services.GetService<ISettingsService>() is { } settings)
                {
                    await settings.SaveAsync().ConfigureAwait(true);
                }
                await _host.StopAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(true);
                if (_host.Services.GetService<INoteRepository>() is IDisposable repo) repo.Dispose();
                _host.Dispose();
            }
        }
        finally
        {
            if (_ownsMutex && _singleInstanceMutex is not null)
            {
                try { _singleInstanceMutex.ReleaseMutex(); } catch { /* nothing to do */ }
                _singleInstanceMutex.Dispose();
            }
            await Log.CloseAndFlushAsync().ConfigureAwait(true);
            base.OnExit(e);
        }
    }
}
