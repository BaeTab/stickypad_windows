using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using LiteDB;
using StickyPad.Models;
using StickyPad.Resources;
using StickyPad.Services;
using StickyPad.Utils;

namespace StickyPad.Tests;

public class SchemaMigratorTests
{
    private static LiteDatabase NewDb() => new(new MemoryStream());
    private static readonly (int, Action<LiteDatabase>)[] None = Array.Empty<(int, Action<LiteDatabase>)>();

    [Fact]
    public void FreshDb_StampedToBaseline_AndIdempotent()
    {
        using var db = NewDb();
        Assert.Equal(0, db.UserVersion);            // 표시 전
        SchemaMigrator.Migrate(db, 1, None);
        Assert.Equal(1, db.UserVersion);            // baseline
        SchemaMigrator.Migrate(db, 1, None);        // 다시 실행해도
        Assert.Equal(1, db.UserVersion);            // 그대로(idempotent)
    }

    [Fact]
    public void RunsMigrationsInOrder()
    {
        using var db = NewDb();
        var applied = new List<int>();
        var migrations = new (int, Action<LiteDatabase>)[]
        {
            (1, _ => applied.Add(1)),
            (2, _ => applied.Add(2)),
        };
        SchemaMigrator.Migrate(db, 3, migrations);
        Assert.Equal(new[] { 1, 2 }, applied);      // v1→v2→v3 순서대로
        Assert.Equal(3, db.UserVersion);
    }

    [Fact]
    public void ThrowsOnMissingMigration()
    {
        using var db = NewDb();
        var migrations = new (int, Action<LiteDatabase>)[] { (1, _ => { }) }; // 2→3 없음
        Assert.Throws<InvalidOperationException>(() => SchemaMigrator.Migrate(db, 3, migrations));
    }

    [Fact]
    public void NewerDb_LeftUntouched()
    {
        using var db = NewDb();
        db.UserVersion = 5;                          // 앱보다 최신(다운그레이드 상황)
        SchemaMigrator.Migrate(db, 1, None);
        Assert.Equal(5, db.UserVersion);             // 손대지 않음
    }
}

public class SecurityHardeningTests
{
    [Fact]
    public void Import_RichTextXaml_DowngradedToPlainText_KillsXamlSink()
    {
        var id = Guid.NewGuid();
        var note = new Note
        {
            Id = id,
            Format = NoteContentFormat.RichTextXaml,
            Content = "<Section><ObjectDataProvider MethodName=\"Start\"/></Section>",
            PlainText = "안녕",
            LinkedFilePath = @"C:\Users\x\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup\evil.cmd",
        };

        var r = BackupService.SanitizeImportedNote(note);

        Assert.Equal(NoteContentFormat.PlainText, r.Format);   // XAML 로더에 도달 안 함
        Assert.Equal("안녕", r.Content);                        // 원본 XAML 이 아님
        Assert.DoesNotContain("ObjectDataProvider", r.Content);
        Assert.Null(r.LinkedFilePath);                          // 임의 파일 쓰기 통로 제거
        Assert.Equal(id, r.Id);                                 // Id 유지(정상 복원 위해)
    }

    [Fact]
    public void Import_NonXaml_KeepsContent_StripsLinkedPath()
    {
        var r = BackupService.SanitizeImportedNote(new Note
        {
            Format = NoteContentFormat.Markdown,
            Content = "# hi",
            LinkedFilePath = @"C:\x\a.md",
        });
        Assert.Equal(NoteContentFormat.Markdown, r.Format);
        Assert.Equal("# hi", r.Content);
        Assert.Null(r.LinkedFilePath);
    }

    [Fact]
    public void ExportDocument_Csp_BlocksRemoteResources()
    {
        var note = new Note { Title = "t", Format = NoteContentFormat.PlainText, PlainText = "x", Content = "x" };
        var html = HtmlRenderer.RenderDocument(new[] { note }, "doc");
        Assert.Contains("default-src 'none'; img-src data:; media-src data:; ", html);
        Assert.DoesNotContain("img-src data: https", html);   // 원격 이미지 허용 안 함
    }

    [Theory]
    [InlineData("v1.6.1", true)]
    [InlineData("1.6.1", true)]
    [InlineData("v1.6.1.0", true)]
    [InlineData("v2", true)]
    [InlineData("x\" & calc & \"", false)]   // cmd 인젝션 시도
    [InlineData("1.6.0; whoami", false)]
    [InlineData("", false)]
    public void Update_TagValidation(string tag, bool expected) =>
        Assert.Equal(expected, UpdateService.IsSafeReleaseTag(tag));

    [Fact]
    public void DangerousXaml_GadgetDetected_AndKeptFromParser()
    {
        var payload =
            "<Section xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" " +
            "xmlns:d=\"clr-namespace:System.Diagnostics;assembly=System.Diagnostics.Process\">" +
            "<Section.Resources><ObjectDataProvider x:Key=\"p\" ObjectType=\"{x:Type d:Process}\" MethodName=\"Start\"/></Section.Resources></Section>";

        Assert.True(TextExtraction.ContainsDangerousXaml(payload));
        // 가드가 파서 앞에서 막으므로 실행되지 않고 태그 제거 텍스트만 반환.
        var text = TextExtraction.ToPlainText(payload, NoteContentFormat.RichTextXaml);
        Assert.DoesNotContain("<ObjectDataProvider", text);
    }

    [Fact]
    public void LegitFlowXaml_NotFlagged()
    {
        var legit =
            "<Section xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">" +
            "<Paragraph><Run>hello</Run> <Bold>world</Bold></Paragraph></Section>";
        Assert.False(TextExtraction.ContainsDangerousXaml(legit));   // 오탐 없음
    }

    [Theory]
    [InlineData("https://github.com/BaeTab/stickypad_windows/releases/download/v1.6.1/StickyPad.exe", true)]
    [InlineData("https://objects.githubusercontent.com/abc", true)]
    [InlineData("http://github.com/x", false)]              // 평문
    [InlineData("https://evil.example/StickyPad.exe", false)]
    [InlineData("file:///C:/evil.exe", false)]
    [InlineData(null, false)]
    public void Update_UrlValidation(string? url, bool expected) =>
        Assert.Equal(expected, UpdateService.IsTrustedDownloadUrl(url));
}

public class UpdateIntegrityTests
{
    private const string HelloSha256 = "2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824";

    [Fact]
    public void VerifyChecksum_MatchesCorrectHash_RejectsOthers()
    {
        var data = System.Text.Encoding.UTF8.GetBytes("hello");
        Assert.True(UpdateService.VerifyChecksum(data, HelloSha256 + "  StickyPad.exe"));
        Assert.True(UpdateService.VerifyChecksum(data, HelloSha256.ToUpperInvariant() + "  x")); // 대소문자 무시
        Assert.False(UpdateService.VerifyChecksum(data, new string('0', 64) + "  x"));            // 불일치
        Assert.False(UpdateService.VerifyChecksum(data, "not-a-hash"));                           // hex 없음
        Assert.False(UpdateService.VerifyChecksum(data, null));                                   // 누락 → 거부
    }

    [Fact]
    public void ExtractSha256Hex_ParsesTokenOrNull()
    {
        Assert.Equal(HelloSha256, UpdateService.ExtractSha256Hex(HelloSha256 + "  file.exe"));
        Assert.Null(UpdateService.ExtractSha256Hex("nope"));
        Assert.Null(UpdateService.ExtractSha256Hex(""));
    }

    [Fact]
    public void ParseRelease_PicksExeAndChecksumAssets()
    {
        var json = @"{""tag_name"":""v1.7.0"",""assets"":[
            {""name"":""StickyPad-v1.7.0-win-x64.exe"",""browser_download_url"":""https://github.com/a/b/releases/download/v1.7.0/StickyPad-v1.7.0-win-x64.exe""},
            {""name"":""StickyPad-v1.7.0-win-x64.exe.sha256"",""browser_download_url"":""https://github.com/a/b/releases/download/v1.7.0/StickyPad-v1.7.0-win-x64.exe.sha256""}]}";
        var r = UpdateService.ParseRelease(json);
        Assert.NotNull(r);
        Assert.EndsWith("win-x64.exe", r!.DownloadUrl);
        Assert.EndsWith(".exe.sha256", r.ChecksumUrl);
    }
}

public class LocalizationTests
{
    [Fact]
    public void Strings_ResolvePerCulture()
    {
        var original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("ko");
            Assert.Equal("설정", Strings.Settings_Title);          // ko 위성 어셈블리
            Assert.Equal("저장", Strings.Common_Save);

            CultureInfo.CurrentUICulture = new CultureInfo("en");
            Assert.Equal("Settings", Strings.Settings_Title);      // neutral(영어) 폴백
            Assert.Equal("Save", Strings.Common_Save);

            // 정의되지 않은 문화권(예: 프랑스어)은 neutral(영어)로 폴백.
            CultureInfo.CurrentUICulture = new CultureInfo("fr");
            Assert.Equal("Settings", Strings.Settings_Title);
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }
}

public class HotkeyGestureTests
{
    [Theory]
    [InlineData("Ctrl+Shift+N", Key.N, ModifierKeys.Control | ModifierKeys.Shift)]
    [InlineData("ctrl+l", Key.L, ModifierKeys.Control)]
    [InlineData("Ctrl+Alt+1", Key.D1, ModifierKeys.Control | ModifierKeys.Alt)]
    public void TryParse_ValidGestures(string gesture, Key expectedKey, ModifierKeys expectedMods)
    {
        Assert.True(HotkeyGesture.TryParse(gesture, out var key, out var mods));
        Assert.Equal(expectedKey, key);
        Assert.Equal(expectedMods, mods);
    }

    [Theory]
    [InlineData("N")]          // no modifier
    [InlineData("")]           // empty
    [InlineData("Ctrl+")]      // no key
    [InlineData("Ctrl+Bogus")] // unknown key
    public void TryParse_Rejects(string gesture)
    {
        Assert.False(HotkeyGesture.TryParse(gesture, out _, out _));
    }

    [Fact]
    public void Format_RoundTrips()
    {
        var text = HotkeyGesture.Format(Key.N, ModifierKeys.Control | ModifierKeys.Shift);
        Assert.Equal("Ctrl+Shift+N", text);
        Assert.True(HotkeyGesture.TryParse(text, out var key, out var mods));
        Assert.Equal(Key.N, key);
        Assert.Equal(ModifierKeys.Control | ModifierKeys.Shift, mods);
    }
}

public class TextExtractionTests
{
    [Fact]
    public void ExtractTags_DedupesAndKeepsOrder()
    {
        var tags = TextExtraction.ExtractTags("todo #work stuff #idea more #work");
        Assert.Equal(new[] { "work", "idea" }, tags);
    }

    [Fact]
    public void ExtractTags_NoneWhenAbsent()
    {
        Assert.Empty(TextExtraction.ExtractTags("plain text, email a#b not a tag"));
    }

    [Fact]
    public void FindLinks_FindsWikiTitle()
    {
        var links = TextExtraction.FindLinks("see [[My Note]] here").ToList();
        Assert.Single(links);
        Assert.Equal("My Note", links[0].Title);
    }

    [Fact]
    public void FindUrls_StripsTrailingPunctuation()
    {
        var urls = TextExtraction.FindUrls("visit https://example.com/path, now").ToList();
        Assert.Single(urls);
        Assert.Equal("https://example.com/path", urls[0].Url);
    }

    [Fact]
    public void DeriveTitle_UsesFirstNonEmptyLine()
    {
        Assert.Equal("Hello world", TextExtraction.DeriveTitle("\n\n  Hello world \nmore"));
    }

    [Fact]
    public void ExcerptOf_TruncatesLongText()
    {
        var excerpt = TextExtraction.ExcerptOf(new string('a', 300));
        Assert.EndsWith("…", excerpt);
        Assert.True(excerpt.Length <= 161);
    }

    [Fact]
    public void ToPlainText_StripsMarkdown()
    {
        var plain = TextExtraction.ToPlainText("# Heading\n\nsome **bold** text", NoteContentFormat.Markdown);
        Assert.Contains("Heading", plain);
        Assert.Contains("bold", plain);
        Assert.DoesNotContain("#", plain);
        Assert.DoesNotContain("**", plain);
    }

    [Fact]
    public void ToPlainText_StripsHtmlTags()
    {
        var plain = TextExtraction.ToPlainText("<p>hi <b>there</b></p>", NoteContentFormat.Html);
        Assert.Equal("hi there", plain.Trim());
    }

    [Fact]
    public void ToPlainText_PlainPassthrough()
    {
        Assert.Equal("just text", TextExtraction.ToPlainText("just text", NoteContentFormat.PlainText));
    }
}

public class HtmlRendererTests
{
    private static NoteTheme Theme => NotePalette.For(NoteColor.Yellow);

    [Fact]
    public void Render_Markdown_ProducesHtmlAndCsp()
    {
        var html = HtmlRenderer.Render("# Hi\n\ntext", NoteContentFormat.Markdown, Theme);
        Assert.Contains("<h1", html);
        Assert.Contains("Hi", html);
        Assert.Contains("Content-Security-Policy", html);
        Assert.DoesNotContain("script-src", html); // scripts not allowed
    }

    [Fact]
    public void Render_Html_PassesThrough()
    {
        var html = HtmlRenderer.Render("<b>x</b>", NoteContentFormat.Html, Theme);
        Assert.Contains("<b>x</b>", html);
    }

    [Fact]
    public void Render_PlainText_IsEscaped()
    {
        var html = HtmlRenderer.Render("a < b & c", NoteContentFormat.PlainText, Theme);
        Assert.Contains("a &lt; b &amp; c", html);
    }
}

public class UpdateServiceVersionTests
{
    [Fact]
    public void Compare_NewerIsGreater()
    {
        Assert.True(UpdateService.Compare(new Version(1, 3, 0), new Version(1, 2, 9)) > 0);
        Assert.True(UpdateService.Compare(new Version(2, 0, 0), new Version(1, 9, 9)) > 0);
    }

    [Fact]
    public void Compare_IgnoresRevision()
    {
        Assert.Equal(0, UpdateService.Compare(new Version(1, 3, 0, 0), new Version(1, 3, 0)));
    }

    [Theory]
    [InlineData("v1.2.3", 1, 2, 3)]
    [InlineData("1.4.0", 1, 4, 0)]
    public void ParseVersion_HandlesTag(string tag, int maj, int min, int build)
    {
        var v = UpdateService.ParseVersion(tag);
        Assert.NotNull(v);
        Assert.Equal(maj, v!.Major);
        Assert.Equal(min, v.Minor);
        Assert.Equal(build, v.Build);
    }

    [Fact]
    public void ParseVersion_NullOnGarbage() => Assert.Null(UpdateService.ParseVersion("nope"));

    [Fact]
    public void ParseRelease_ReadsTagAndAsset()
    {
        const string json = """
        {
          "tag_name": "v1.4.0",
          "assets": [
            { "name": "notes.txt", "browser_download_url": "https://x/notes.txt" },
            { "name": "StickyPad-v1.4.0-win-x64.exe", "browser_download_url": "https://x/app.exe" }
          ]
        }
        """;
        var info = UpdateService.ParseRelease(json);
        Assert.NotNull(info);
        Assert.Equal("v1.4.0", info!.Tag);
        Assert.Equal(new Version(1, 4, 0), info.Version);
        Assert.Equal("https://x/app.exe", info.DownloadUrl);
    }
}

public class NoteRepositoryTests
{
    private static string TempDbPath() =>
        Path.Combine(Path.GetTempPath(), "stickypad-test-" + Guid.NewGuid().ToString("N") + ".db");

    private static async Task WithRepo(Func<NoteRepository, Task> body)
    {
        var path = TempDbPath();
        try
        {
            using (var repo = new NoteRepository(path))
            {
                await body(repo);
            }
        }
        finally
        {
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }

    private static Note NewNote(string content = "hello") => new()
    {
        Content = content,
        PlainText = content,
        Title = content,
    };

    [Fact]
    public Task Upsert_ThenGetAll_ReturnsNote() => WithRepo(async repo =>
    {
        var note = NewNote();
        await repo.UpsertAsync(note);
        var all = await repo.GetAllAsync();
        Assert.Single(all);
        Assert.Equal(note.Id, all[0].Id);
    });

    [Fact]
    public Task Delete_MovesToTrash() => WithRepo(async repo =>
    {
        var note = NewNote();
        await repo.UpsertAsync(note);
        await repo.DeleteAsync(note.Id);

        Assert.Empty(await repo.GetAllAsync());
        Assert.Null(await repo.GetByIdAsync(note.Id));
        var trashed = await repo.GetTrashedAsync();
        Assert.Single(trashed);
        Assert.True(trashed[0].IsDeleted);
    });

    [Fact]
    public Task Restore_BringsNoteBack() => WithRepo(async repo =>
    {
        var note = NewNote();
        await repo.UpsertAsync(note);
        await repo.DeleteAsync(note.Id);
        await repo.RestoreAsync(note.Id);

        Assert.Single(await repo.GetAllAsync());
        Assert.Empty(await repo.GetTrashedAsync());
    });

    [Fact]
    public Task Purge_RemovesEntirely() => WithRepo(async repo =>
    {
        var note = NewNote();
        await repo.UpsertAsync(note);
        await repo.DeleteAsync(note.Id);
        await repo.PurgeAsync(note.Id);

        Assert.Empty(await repo.GetAllAsync());
        Assert.Empty(await repo.GetTrashedAsync());
    });

    [Fact]
    public Task SaveContent_DoesNotResurrectDeletedNote() => WithRepo(async repo =>
    {
        var note = NewNote("original");
        await repo.UpsertAsync(note);
        await repo.DeleteAsync(note.Id);

        // A stale open-window copy flushes with IsDeleted=false — must not undelete.
        var stale = NewNote("edited later");
        stale.Id = note.Id;
        stale.IsDeleted = false;
        await repo.SaveContentAsync(stale);

        Assert.Null(await repo.GetByIdAsync(note.Id));      // still hidden
        var trashed = await repo.GetTrashedAsync();
        Assert.Single(trashed);
        Assert.True(trashed[0].IsDeleted);
    });

    [Fact]
    public Task PurgeTrashedOlderThan_RemovesOldOnly() => WithRepo(async repo =>
    {
        var oldNote = NewNote("old");
        await repo.UpsertAsync(oldNote);
        await repo.DeleteAsync(oldNote.Id);

        var recent = NewNote("recent");
        await repo.UpsertAsync(recent);
        await repo.DeleteAsync(recent.Id);

        // Cutoff in the future removes everything trashed before now.
        var purged = await repo.PurgeTrashedOlderThanAsync(DateTime.UtcNow.AddDays(1));
        Assert.Equal(2, purged);
        Assert.Empty(await repo.GetTrashedAsync());
    });

    [Fact]
    public Task FindByLinkedPath_MatchesCaseInsensitiveAndSkipsTrash() => WithRepo(async repo =>
    {
        var note = NewNote("linked");
        note.LinkedFilePath = @"C:\Docs\Report.md";
        await repo.UpsertAsync(note);

        // 대소문자 무시 매칭.
        var found = await repo.FindByLinkedPathAsync(@"c:\docs\report.md");
        Assert.NotNull(found);
        Assert.Equal(note.Id, found!.Id);

        // 연동 경로가 없는 조회는 null.
        Assert.Null(await repo.FindByLinkedPathAsync(@"C:\Docs\Other.md"));

        // 휴지통으로 가면 더 이상 매칭되지 않는다(원본 파일은 코드가 건드리지 않음).
        await repo.DeleteAsync(note.Id);
        Assert.Null(await repo.FindByLinkedPathAsync(@"C:\Docs\Report.md"));
    });
}

public class LinkedFileTests
{
    [Theory]
    [InlineData("a.md")]
    [InlineData("a.MARKDOWN")]
    [InlineData("a.txt")]
    [InlineData("a.log")]
    [InlineData("a.unknownext")]
    public void FormatFor_AlwaysMarkdown_SoRawTextRoundTrips(string name)
    {
        // 연동 텍스트 파일은 XAML 오염을 막기 위해 항상 Markdown 소스로 다룬다.
        Assert.Equal(NoteContentFormat.Markdown, LinkedFile.FormatFor(name));
    }

    [Theory]
    [InlineData("notes.md", true)]
    [InlineData("readme.txt", true)]
    [InlineData("app.exe", false)]
    [InlineData("photo.png", false)]
    [InlineData("archive.zip", false)]
    public void IsSupported_FiltersBinaries(string name, bool expected)
    {
        Assert.Equal(expected, LinkedFile.IsSupported(name));
    }

    [Fact]
    public async Task WriteThenRead_RoundTripsWithoutBom()
    {
        var path = Path.Combine(Path.GetTempPath(), "stickypad-linked-" + Guid.NewGuid().ToString("N") + ".md");
        try
        {
            const string text = "# 제목\n\n본문 with **bold** 🎯";
            var writeUtc = await LinkedFile.WriteAsync(path, text);

            // BOM 이 붙지 않아야 한다(UTF-8, no BOM).
            var bytes = await File.ReadAllBytesAsync(path);
            Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);

            var (content, readUtc) = await LinkedFile.ReadAsync(path);
            Assert.Equal(text, content);
            Assert.Equal(writeUtc, readUtc);
        }
        finally
        {
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task Read_DetectsBomEncodedFile()
    {
        var path = Path.Combine(Path.GetTempPath(), "stickypad-bom-" + Guid.NewGuid().ToString("N") + ".md");
        try
        {
            const string text = "héllo 안녕";
            await File.WriteAllTextAsync(path, text, new System.Text.UTF8Encoding(true)); // with BOM
            var (content, _) = await LinkedFile.ReadAsync(path);
            Assert.Equal(text, content); // BOM 은 벗겨져 내용만 남아야 한다
        }
        finally
        {
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }
}

public class ExportNamingTests
{
    [Theory]
    [InlineData("Shopping list", "Shopping list")]
    [InlineData("a/b:c*d?", "a_b_c_d_")]           // 금지 문자는 _ 로
    [InlineData("///", "___")]                      // 전부 금지 문자여도 _ 로 치환되면 유효
    [InlineData("  spaced  ", "spaced")]            // 앞뒤 공백 제거
    [InlineData("trailing...", "trailing")]         // 끝의 점 제거
    public void SafeFileName_Sanitizes(string title, string expected)
    {
        Assert.Equal(expected, ExportNaming.SafeFileName(title));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]      // 공백만 → 비면 fallback
    [InlineData("...")]      // 점만 → 트림하면 비어 fallback
    public void SafeFileName_FallsBackWhenEmpty(string title)
    {
        Assert.Equal("note", ExportNaming.SafeFileName(title));
    }

    [Fact]
    public void SafeFileName_CapsLength()
    {
        var name = ExportNaming.SafeFileName(new string('x', 500));
        Assert.True(name.Length <= 80);
    }

    [Theory]
    [InlineData("CON", "_CON")]
    [InlineData("nul", "_nul")]
    [InlineData("COM1", "_COM1")]
    [InlineData("Contract", "Contract")]   // 예약어의 접두사일 뿐이면 그대로
    public void SafeFileName_EscapesReservedDeviceNames(string title, string expected)
    {
        Assert.Equal(expected, ExportNaming.SafeFileName(title));
    }

    [Fact]
    public void UniqueFileName_DisambiguatesCollisions()
    {
        var taken = new HashSet<string>();
        Assert.Equal("note.md", ExportNaming.UniqueFileName("note", ".md", taken));
        Assert.Equal("note (2).md", ExportNaming.UniqueFileName("note", ".md", taken));
        Assert.Equal("note (3).md", ExportNaming.UniqueFileName("note", ".md", taken));
        Assert.Equal("other.md", ExportNaming.UniqueFileName("other", "md", taken)); // 앞에 점 없어도 됨
    }
}

public class HtmlRendererDocumentTests
{
    private static Note PlainNote(string title, string plain) => new()
    {
        Title = title,
        PlainText = plain,
        Format = NoteContentFormat.PlainText,
        Content = plain,
    };

    [Fact]
    public void RenderDocument_IncludesTitlesAndDocHeading()
    {
        var notes = new[] { PlainNote("First", "one"), PlainNote("Second", "two") };
        var html = HtmlRenderer.RenderDocument(notes, "내 노트");

        Assert.Contains("내 노트", html);
        Assert.Contains(">First<", html);
        Assert.Contains(">Second<", html);
        // 노트마다 article 하나씩.
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(html, "<article").Count);
    }

    [Fact]
    public void RenderDocument_EscapesPlainTextBody()
    {
        var notes = new[] { PlainNote("XSS", "<script>alert(1)</script>") };
        var html = HtmlRenderer.RenderDocument(notes, "doc");

        // PlainText 본문은 이스케이프돼 실행 가능한 태그가 남지 않아야 한다.
        Assert.DoesNotContain("<script>alert(1)</script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public void RenderDocument_RendersMarkdownBody()
    {
        var note = new Note
        {
            Title = "MD",
            Format = NoteContentFormat.Markdown,
            Content = "# Heading\n\n- item",
            PlainText = "Heading\nitem",
        };
        var html = HtmlRenderer.RenderDocument(new[] { note }, "doc");

        Assert.Contains("<h1", html);   // Markdig 로 헤딩 렌더
        Assert.Contains("<li", html);   // 목록 렌더
    }
}
