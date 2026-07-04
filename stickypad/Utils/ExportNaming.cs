using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace StickyPad.Utils;

/// 노트 제목을 파일 시스템에 안전한 파일명으로 바꾸고, 한 폴더 안에서 중복되지 않게 만든다.
public static class ExportNaming
{
    private const int MaxLength = 80;

    /// 제목을 안전한 파일명(확장자 제외)으로 정규화한다. 빈 제목·정규화 후 빈 문자열이면 fallback 을 쓴다.
    public static string SafeFileName(string? title, string fallback = "note")
    {
        var trimmed = (title ?? string.Empty).Trim();
        if (trimmed.Length == 0) return fallback;

        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(trimmed.Length);
        foreach (var ch in trimmed)
        {
            var bad = ch is '\r' or '\n' or '\t' || Array.IndexOf(invalid, ch) >= 0;
            sb.Append(bad ? '_' : ch);
        }

        // 윈도우는 이름 끝의 점·공백을 허용하지 않는다.
        var name = sb.ToString().TrimEnd('.', ' ');
        if (name.Length > MaxLength) name = name[..MaxLength].TrimEnd('.', ' ');
        if (name.Length == 0) return fallback;

        // 예약된 장치 이름(CON, NUL, COM1 …)은 확장자가 붙어도 여전히 예약이라 앞에 _ 를 붙여 회피.
        return IsReservedDeviceName(name) ? "_" + name : name;
    }

    private static bool IsReservedDeviceName(string name)
    {
        // 예약 여부는 첫 점 앞부분만으로 판단(예: "CON.txt" 도 예약).
        var stem = name;
        var dot = stem.IndexOf('.');
        if (dot >= 0) stem = stem[..dot];
        return Array.Exists(ReservedNames, r => string.Equals(r, stem, StringComparison.OrdinalIgnoreCase));
    }

    private static readonly string[] ReservedNames =
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// baseName + extension 이 taken 집합(소문자 비교)에서 유일하도록, 필요하면 " (2)", " (3)" 을 붙인다.
    /// 반환한 파일명은 taken 에 추가된다.
    public static string UniqueFileName(string baseName, string extension, ISet<string> taken)
    {
        var ext = extension.StartsWith('.') ? extension : "." + extension;
        var candidate = baseName + ext;
        var i = 2;
        while (taken.Contains(candidate.ToLowerInvariant()))
        {
            candidate = $"{baseName} ({i}){ext}";
            i++;
        }
        taken.Add(candidate.ToLowerInvariant());
        return candidate;
    }
}
