using System;
using System.IO;
using System.Text.RegularExpressions;

namespace StickyPad.Utils;

/// 클라우드 동기화 충돌 사본 파일명 감지(Dropbox/OneDrive/Syncthing). 순수 함수 — 파일시스템에
/// 직접 접근하지 않고, OneDrive 판정에 필요한 "원본이 실존하는가"만 콜백으로 주입받는다(§3.8).
public static class ConflictCopyDetector
{
    private static readonly Regex DropboxEn = new(
        @"\(.+ conflicted copy( \d{4}-\d{2}-\d{2})?( \(\d+\))?\)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex DropboxKo = new(
        @"\(충돌[^)]*사본[^)]*\)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex Syncthing = new(
        @"\.sync-conflict-\d{8}-\d{6}(-\w+)?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // 기계명은 프로세스 수명 동안 바뀌지 않으므로 static readonly 로 한 번만 컴파일해 둔다.
    private static readonly Regex OneDriveSuffix = new(
        "-" + Regex.Escape(Environment.MachineName) + @"(-\d+)?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// fileName 은 확장자 포함 파일명(경로 아님). siblingExists 는 "그 파일명이 볼트에 실존하는가" 콜백
    /// — OneDrive 패턴은 하이픈 파일명 오탐이 잦아 기계명 일치 + 원본 실존까지 확인해야 하고,
    /// 나머지 3종(Dropbox EN/KO, Syncthing)은 파일명만으로 판정한다(원본이 이미 정리됐을 수도 있으므로).
    public static bool IsConflictCopy(string fileName, Func<string, bool> siblingExists)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);

        if (DropboxEn.IsMatch(stem) || DropboxKo.IsMatch(stem) || Syncthing.IsMatch(stem))
            return true;

        var match = OneDriveSuffix.Match(stem);
        if (match.Success)
        {
            var original = stem[..match.Index];
            if (original.Length > 0 && siblingExists(original + ext))
                return true;
        }

        return false;
    }
}
