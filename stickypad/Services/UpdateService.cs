using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
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
    private const string ChecksumSuffix = "win-x64.exe.sha256";

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

            if (string.IsNullOrEmpty(release.DownloadUrl) || string.IsNullOrEmpty(release.ChecksumUrl))
            {
                // 검증할 체크섬이 없는 릴리즈는 제안하지 않는다(무결성 확인 불가).
                _logger.LogWarning("Newer release {Tag} lacks exe/sha256 asset", release.Tag);
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
        // 방어: 태그·URL·체크섬 URL 검증. 태그(GitHub 값)를 파일 경로·스크립트에 그대로 넣지 않고,
        // 다운로드·체크섬 출처는 https + GitHub 호스트로 제한한다.
        if (!IsSafeReleaseTag(release.Tag) || !IsTrustedDownloadUrl(release.DownloadUrl)
            || !IsTrustedDownloadUrl(release.ChecksumUrl))
        {
            _logger.LogWarning("Refusing update: untrusted tag/url/checksum ({Tag})", release.Tag);
            Report(Strings.Update_ApplyFailed, MessageBoxImage.Warning);
            return;
        }

        byte[] bytes;
        try
        {
            bytes = await Http.GetByteArrayAsync(release.DownloadUrl!).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Update download failed");
            Report(Strings.Update_DownloadFailed, MessageBoxImage.Warning);
            return;
        }

        // 무결성 검증 — 릴리즈의 .sha256 자산과 대조. 불일치·누락 시 적용을 거부한다(fail-closed).
        try
        {
            var checksumText = await Http.GetStringAsync(release.ChecksumUrl!).ConfigureAwait(false);
            if (!VerifyChecksum(bytes, checksumText))
            {
                _logger.LogError("Update integrity verification FAILED for {Tag}", release.Tag);
                Report(Strings.Update_IntegrityFailed, MessageBoxImage.Warning);
                return;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Update checksum fetch/verify failed");
            Report(Strings.Update_IntegrityFailed, MessageBoxImage.Warning);
            return;
        }

        // 검증 통과 후에만 스테이징. 예측 불가한 전용 폴더(TOCTOU 완화, 파일명에 태그 미포함).
        var stageDir = Path.Combine(Path.GetTempPath(), "StickyPad-update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stageDir);
        var tempExe = Path.Combine(stageDir, "StickyPad-update.exe");
        try
        {
            await File.WriteAllBytesAsync(tempExe, bytes).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Update staging write failed");
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
            LaunchReplacer(stageDir, tempExe, target);
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

    private static void LaunchReplacer(string stageDir, string newExe, string targetExe)
    {
        var pid = Environment.ProcessId;
        var batPath = Path.Combine(stageDir, "apply-update.cmd");
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

        string? url = null, checksumUrl = null;
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
                if (name is null) continue;
                var dl = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                if (name.EndsWith(ChecksumSuffix, StringComparison.OrdinalIgnoreCase)) checksumUrl ??= dl;
                else if (name.EndsWith(AssetSuffix, StringComparison.OrdinalIgnoreCase)) url ??= dl;
            }
        }
        return new ReleaseInfo(tag, version, url, checksumUrl);
    }

    internal static Version? ParseVersion(string tag)
    {
        var t = tag.TrimStart('v', 'V').Trim();
        return Version.TryParse(t, out var v) ? v : null;
    }

    /// GitHub 태그가 예상 버전 형식(v1.2.3 등)인지. 파일 경로·스크립트에 넣기 전 검증.
    internal static bool IsSafeReleaseTag(string? tag) =>
        !string.IsNullOrEmpty(tag) && Regex.IsMatch(tag, @"^v?\d+(\.\d+){0,3}$");

    /// 다운로드 URL 이 https 이고 GitHub 릴리즈 호스트인지. 임의 오리진 다운로드 차단.
    internal static bool IsTrustedDownloadUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u)
        && u.Scheme == Uri.UriSchemeHttps
        && (u.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            || u.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase));

    /// 다운로드한 바이트의 SHA-256 이 체크섬 파일("<64자리 hex>  파일명")의 값과 일치하는지.
    internal static bool VerifyChecksum(byte[] data, string? checksumFileText)
    {
        var expected = ExtractSha256Hex(checksumFileText);
        if (expected is null) return false;
        var actual = Convert.ToHexString(SHA256.HashData(data));
        return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
    }

    /// 체크섬 파일 텍스트에서 첫 64자리 16진수(SHA-256) 토큰을 추출. 없으면 null.
    internal static string? ExtractSha256Hex(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        var m = Regex.Match(text, @"\b([0-9a-fA-F]{64})\b");
        return m.Success ? m.Groups[1].Value : null;
    }

    private static void Report(string message, MessageBoxImage icon) =>
        Application.Current.Dispatcher.Invoke(() =>
            MessageBox.Show(message, "StickyPad", MessageBoxButton.OK, icon));

    private static bool Ask(string message) =>
        Application.Current.Dispatcher.Invoke(() =>
            MessageBox.Show(message, Strings.Update_ConfirmCaption, MessageBoxButton.OKCancel, MessageBoxImage.Question)
            == MessageBoxResult.OK);

    internal sealed record ReleaseInfo(string Tag, Version? Version, string? DownloadUrl, string? ChecksumUrl);
}
