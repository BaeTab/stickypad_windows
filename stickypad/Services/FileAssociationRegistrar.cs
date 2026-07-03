using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace StickyPad.Services;

/// StickyPad 를 .md / .markdown 파일의 '연결 프로그램' 목록에 등록한다(HKCU, 관리자 권한 불필요).
/// 기본 연결을 가로채지 않고 OpenWithProgids 로만 추가 — 사용자가 직접 '항상 이 앱으로 열기'를
/// 고르면 더블클릭으로도 열린다. 우클릭 → 연결 프로그램 목록에는 즉시 나타난다.
public static class FileAssociationRegistrar
{
    private const string ProgId = "StickyPad.Markdown";
    private static readonly string[] Extensions = { ".md", ".markdown" };

    private const int SHCNE_ASSOCCHANGED = 0x08000000;
    private const int SHCNF_IDLIST = 0x0000;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern void SHChangeNotify(int wEventId, int uFlags, IntPtr dwItem1, IntPtr dwItem2);

    /// exePath 로 실행되는 StickyPad 를 등록. 이미 같은 내용이면 아무것도 하지 않는다.
    /// 모든 실패는 조용히 삼킨다 — 파일 연결은 부가 기능이며 앱 동작에 필수가 아니다.
    public static void Ensure(string exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath)) return;

        try
        {
            var openCommand = $"\"{exePath}\" \"%1\"";
            using var classes = Registry.CurrentUser.OpenSubKey(@"Software\Classes", writable: true);
            if (classes is null) return;

            // 이미 동일하게 등록돼 있으면 스킵(쉘 새로고침 남발 방지).
            using (var existing = classes.OpenSubKey($@"{ProgId}\shell\open\command"))
            {
                if (existing?.GetValue(null) as string == openCommand)
                {
                    EnsureOpenWithProgids(classes);
                    return;
                }
            }

            using (var prog = classes.CreateSubKey(ProgId))
            {
                prog.SetValue(null, "Markdown Document");
                using (var icon = prog.CreateSubKey("DefaultIcon"))
                    icon.SetValue(null, $"\"{exePath}\",0");
                using (var cmd = prog.CreateSubKey(@"shell\open\command"))
                    cmd.SetValue(null, openCommand);
            }

            EnsureOpenWithProgids(classes);
            SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
        }
        catch
        {
            // 레지스트리 접근 실패 등 — 무시.
        }
    }

    private static void EnsureOpenWithProgids(RegistryKey classes)
    {
        foreach (var ext in Extensions)
        {
            try
            {
                using var progids = classes.CreateSubKey($@"{ext}\OpenWithProgids");
                progids.SetValue(ProgId, Array.Empty<byte>(), RegistryValueKind.None);
            }
            catch
            {
                // 개별 확장자 실패는 무시.
            }
        }
    }
}
