using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.Logging;
using StickyPad.Resources;

namespace StickyPad.Services;

public sealed class UpdateService : IUpdateService
{
    private const string LatestReleaseApi =
        "https://api.github.com/repos/BaeTab/stickypad_windows/releases/latest";
    private const string AssetSuffix = "win-x64.exe";

    private static readonly HttpClient Http = CreateClient();
    private readonly ILogger<UpdateService> _logger;
    private bool _inProgress;

    public UpdateService(ILogger<UpdateService> logger) => _logger = logger;

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        // GitHub rejects requests without a User-Agent.
        http.DefaultRequestHeaders.UserAgent.ParseAdd("StickyPad-Updater");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return http;
    }

    public async Task CheckAsync(bool userInitiated)
    {
        if (_inProgress) return;
        _inProgress = true;
        try
        {
            var current = CurrentVersion();

            ReleaseInfo? release;
            try
            {
                var json = await Http.GetStringAsync(LatestReleaseApi).ConfigureAwait(false);
                release = ParseRelease(json);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Update check failed");
                if (userInitiated) Report(Strings.Update_CheckFailed, MessageBoxImage.Warning);
                return;
            }

            if (release?.Version is null)
            {
                if (userInitiated) Report(Strings.Update_InfoUnreadable, MessageBoxImage.Warning);
                return;
            }

            if (Compare(release.Version, current) <= 0)
            {
                _logger.LogInformation("Up to date (current {Current}, latest {Latest})", current, release.Tag);
                if (userInitiated) Report(string.Format(Strings.Update_UpToDateFormat, current.Major, current.Minor, current.Build), MessageBoxImage.Information);
                return;
            }

            if (string.IsNullOrEmpty(release.DownloadUrl))
            {
                _logger.LogWarning("Newer release {Tag} has no {Suffix} asset", release.Tag, AssetSuffix);
                if (userInitiated) Report(string.Format(Strings.Update_AssetNotFoundFormat, release.Tag), MessageBoxImage.Warning);
                return;
            }

            var proceed = Ask(string.Format(Strings.Update_AvailablePromptFormat, release.Tag));
            if (!proceed) return;

            await DownloadAndApplyAsync(release).ConfigureAwait(false);
        }
        finally
        {
            _inProgress = false;
        }
    }

    private async Task DownloadAndApplyAsync(ReleaseInfo release)
    {
        var tempExe = Path.Combine(Path.GetTempPath(), $"StickyPad-update-{release.Tag}.exe");
        try
        {
            var bytes = await Http.GetByteArrayAsync(release.DownloadUrl!).ConfigureAwait(false);
            await File.WriteAllBytesAsync(tempExe, bytes).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Update download failed");
            Report(Strings.Update_DownloadFailed, MessageBoxImage.Warning);
            return;
        }

        var target = Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrEmpty(target))
        {
            Report(Strings.Update_ExecutablePathUnknown, MessageBoxImage.Warning);
            return;
        }

        try
        {
            LaunchReplacer(tempExe, target);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to launch the update helper");
            Report(Strings.Update_ApplyFailed, MessageBoxImage.Warning);
            return;
        }

        // Quit so the running exe unlocks; the helper then swaps the file and relaunches.
        Application.Current.Dispatcher.Invoke(() => Application.Current.Shutdown());
    }

    private static void LaunchReplacer(string newExe, string targetExe)
    {
        var pid = Environment.ProcessId;
        var batPath = Path.Combine(Path.GetTempPath(), $"stickypad-update-{pid}.cmd");
        // Wait for this process to exit (release the lock), swap the exe, relaunch, then self-delete.
        var script =
            "@echo off\r\n" +
            ":wait\r\n" +
            $"tasklist /fi \"PID eq {pid}\" 2>nul | find \"{pid}\" >nul\r\n" +
            "if not errorlevel 1 (\r\n" +
            "  timeout /t 1 /nobreak >nul\r\n" +
            "  goto wait\r\n" +
            ")\r\n" +
            $"copy /y \"{newExe}\" \"{targetExe}\" >nul\r\n" +
            $"del \"{newExe}\" >nul 2>&1\r\n" +
            $"start \"\" \"{targetExe}\"\r\n" +
            "del \"%~f0\"\r\n";
        File.WriteAllText(batPath, script, new UTF8Encoding(false));

        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{batPath}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden,
        });
    }

    private static Version CurrentVersion() =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    /// Compares two versions on Major.Minor.Build only (ignores the revision field).
    internal static int Compare(Version a, Version b)
    {
        if (a.Major != b.Major) return a.Major.CompareTo(b.Major);
        var an = Math.Max(0, a.Minor); var bn = Math.Max(0, b.Minor);
        if (an != bn) return an.CompareTo(bn);
        return Math.Max(0, a.Build).CompareTo(Math.Max(0, b.Build));
    }

    internal static ReleaseInfo? ParseRelease(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("tag_name", out var tagEl)) return null;
        var tag = tagEl.GetString() ?? string.Empty;
        var version = ParseVersion(tag);

        string? url = null;
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
                if (name is not null && name.EndsWith(AssetSuffix, StringComparison.OrdinalIgnoreCase))
                {
                    url = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                    break;
                }
            }
        }
        return new ReleaseInfo(tag, version, url);
    }

    internal static Version? ParseVersion(string tag)
    {
        var t = tag.TrimStart('v', 'V').Trim();
        return Version.TryParse(t, out var v) ? v : null;
    }

    private static void Report(string message, MessageBoxImage icon) =>
        Application.Current.Dispatcher.Invoke(() =>
            MessageBox.Show(message, "StickyPad", MessageBoxButton.OK, icon));

    private static bool Ask(string message) =>
        Application.Current.Dispatcher.Invoke(() =>
            MessageBox.Show(message, Strings.Update_ConfirmCaption, MessageBoxButton.OKCancel, MessageBoxImage.Question)
            == MessageBoxResult.OK);

    internal sealed record ReleaseInfo(string Tag, Version? Version, string? DownloadUrl);
}
