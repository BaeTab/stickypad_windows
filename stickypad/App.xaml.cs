using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using StickyPad.Services;
using StickyPad.ViewModels;

namespace StickyPad;

public partial class App : Application
{
    private const string SingleInstanceMutexName = @"Local\StickyPad.SingleInstance";

    private IHost? _host;
    private Mutex? _singleInstanceMutex;
    private bool _ownsMutex;

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
            // Another instance is running — quietly exit. Tray click on the running instance handles activation.
            Shutdown(0);
            return;
        }

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
                services.AddSingleton<ITrayService, TrayService>();
                services.AddTransient<NotesListViewModel>();
            })
            .Build();

        await _host.StartAsync().ConfigureAwait(true);

        try
        {
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

            var settings = _host.Services.GetRequiredService<ISettingsService>().Current;
            if (settings.GlobalHotkeysEnabled)
            {
                var hotkeys = _host.Services.GetRequiredService<IHotkeyService>();
                hotkeys.Register(
                    newNoteHandler: () => manager.CreateAndShowNew(),
                    openNotesListHandler: manager.OpenNotesList);
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

    protected override async void OnExit(ExitEventArgs e)
    {
        try
        {
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
