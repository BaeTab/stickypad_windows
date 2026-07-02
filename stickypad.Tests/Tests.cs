using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using StickyPad.Models;
using StickyPad.Services;
using StickyPad.Utils;

namespace StickyPad.Tests;

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
}
