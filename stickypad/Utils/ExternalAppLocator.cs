using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace StickyPad.Utils;

/// Obsidian·VS Code 설치를 읽기 전용으로 감지하고 실행한다. 트레이 메뉴 Opened 이벤트마다
/// 호출되므로 가볍게 유지한다(레지스트리 1~2회, 파일 존재 확인 3회 이내, §3.9).
public static class ExternalAppLocator
{
    // ── Obsidian ────────────────────────────────────────────────────────────

    public static bool IsObsidianInstalled() => IsUrlProtocolRegistered("obsidian");

    public static void OpenObsidian(string vaultPath)
    {
        var uri = "obsidian://open?path=" + Uri.EscapeDataString(vaultPath);
        Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
    }

    // ── VS Code ─────────────────────────────────────────────────────────────

    public static bool IsVsCodeAvailable() => FindVsCode() is not null || IsUrlProtocolRegistered("vscode");

    public static string? FindVsCode() => FindVsCode(File.Exists, Environment.GetEnvironmentVariable);

    /// 탐색 순서 그대로(①LOCALAPPDATA ②ProgramFiles ③PATH 의 code.cmd, where 없이 직접 분할 검색) —
    /// 파일시스템·환경변수 조회를 주입 가능하게 분리해 레지스트리 없이 단위 테스트한다.
    public static string? FindVsCode(Func<string, bool> fileExists, Func<string, string?> getEnv)
    {
        var localAppData = getEnv("LOCALAPPDATA");
        if (!string.IsNullOrEmpty(localAppData))
        {
            var candidate = Path.Combine(localAppData, "Programs", "Microsoft VS Code", "Code.exe");
            if (fileExists(candidate)) return candidate;
        }

        var programFiles = getEnv("ProgramFiles");
        if (!string.IsNullOrEmpty(programFiles))
        {
            var candidate = Path.Combine(programFiles, "Microsoft VS Code", "Code.exe");
            if (fileExists(candidate)) return candidate;
        }

        var pathVar = getEnv("PATH");
        if (!string.IsNullOrEmpty(pathVar))
        {
            foreach (var dir in pathVar.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                var candidate = Path.Combine(dir.Trim(), "code.cmd");
                if (fileExists(candidate)) return candidate;
            }
        }

        return null;
    }

    public static void OpenVsCode(string vaultPath)
    {
        var codePath = FindVsCode();
        if (codePath is not null)
        {
            // 셸 문자열 연결 없이 인자 배열로 폴더 경로를 넘긴다.
            var psi = new ProcessStartInfo(codePath) { UseShellExecute = false };
            psi.ArgumentList.Add(vaultPath);
            Process.Start(psi);
            return;
        }

        // 실행 파일을 못 찾으면 프로토콜로 폴백 — 경로 구분자를 URI 관례(/)로 정규화.
        var uri = "vscode://file/" + vaultPath.Replace('\\', '/');
        Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
    }

    // ── 공통 ────────────────────────────────────────────────────────────────

    private static bool IsUrlProtocolRegistered(string scheme)
    {
        try
        {
            using var hkcr = Registry.ClassesRoot.OpenSubKey(scheme);
            if (hkcr?.GetValue("URL Protocol") is not null) return true;
        }
        catch { /* 읽기 실패는 미설치로 간주 */ }

        try
        {
            using var hkcu = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{scheme}");
            if (hkcu?.GetValue("URL Protocol") is not null) return true;
        }
        catch { /* 읽기 실패는 미설치로 간주 */ }

        return false;
    }
}
