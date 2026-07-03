using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using StickyPad.Models;

namespace StickyPad.Utils;

/// 외부 텍스트/마크다운 파일과 연동된 노트의 파일 I/O 헬퍼.
/// 읽기는 BOM 자동 인식, 쓰기는 UTF-8(BOM 없음)으로 통일한다.
public static class LinkedFile
{
    /// 열기 대화상자·드롭·파일 연결에서 허용하는 확장자.
    private static readonly string[] KnownExtensions =
    {
        ".md", ".markdown", ".mkd", ".mdown", ".mdwn", ".mdtext",
        ".txt", ".text", ".log",
    };

    /// 열기 대화상자용 필터 문자열.
    public const string OpenDialogFilter =
        "Markdown / text (*.md;*.markdown;*.txt)|*.md;*.markdown;*.mkd;*.mdown;*.mdwn;*.txt;*.text;*.log|All files (*.*)|*.*";

    /// 절대·정규화된 경로로 변환. 실패하면 원본을 다듬어 반환.
    public static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        try { return Path.GetFullPath(path.Trim()); }
        catch { return path.Trim(); }
    }

    /// 지원하는(=열어서 렌더링할 만한) 텍스트 파일인지. 알 수 없는 확장자도 텍스트로 허용하되
    /// 실행 파일류만 제외한다.
    public static bool IsSupported(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var ext = Path.GetExtension(path);
        foreach (var known in KnownExtensions)
        {
            if (string.Equals(ext, known, StringComparison.OrdinalIgnoreCase)) return true;
        }
        // 확장자를 모르면 명백한 바이너리/실행 파일만 거른다.
        return ext.ToLowerInvariant() is not (".exe" or ".dll" or ".zip" or ".png" or ".jpg" or ".jpeg"
            or ".gif" or ".pdf" or ".mp3" or ".mp4" or ".bin" or ".db");
    }

    /// 연동 노트의 포맷. 텍스트 파일은 항상 Markdown 소스로 다룬다 — 편집기가 원본 텍스트를
    /// 그대로 파일에 되쓸 수 있는 경로(IsMarkup)를 타게 해, 리치 텍스트 XAML 이 파일에 섞여
    /// 저장되는 사고를 원천 차단한다. (플레인 텍스트도 Markdown 으로 렌더하면 거의 동일하게 보인다.)
    public static NoteContentFormat FormatFor(string path)
    {
        _ = path;
        return NoteContentFormat.Markdown;
    }

    /// 파일 텍스트와 마지막 쓰기 시각(UTC)을 읽는다. 편집기가 파일을 잠글 수 있어 몇 번 재시도한다.
    public static async Task<(string Content, DateTime WriteUtc)> ReadAsync(string path)
    {
        IOException? last = null;
        for (var attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                // BOM 자동 인식(UTF-8/16); 공유 읽기로 잠긴 파일도 최대한 읽는다.
                string text;
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
                {
                    text = await reader.ReadToEndAsync().ConfigureAwait(false);
                }
                var writeUtc = File.GetLastWriteTimeUtc(path);
                return (text, writeUtc);
            }
            catch (IOException ex)
            {
                last = ex;
                await Task.Delay(60).ConfigureAwait(false);
            }
        }
        throw last ?? new IOException($"Could not read '{path}'.");
    }

    /// 파일에 텍스트를 UTF-8(BOM 없음)으로 저장하고 새 LastWriteTimeUtc 를 돌려준다.
    /// 디렉터리가 없으면 만든다. 파일이 삭제됐다면 다시 만든다.
    public static async Task<DateTime> WriteAsync(string path, string content)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        await File.WriteAllTextAsync(path, content, encoding).ConfigureAwait(false);
        return File.GetLastWriteTimeUtc(path);
    }
}
