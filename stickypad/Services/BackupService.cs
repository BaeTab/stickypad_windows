using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Win32;
using StickyPad.Models;
using StickyPad.Resources;
using StickyPad.Utils;

namespace StickyPad.Services;

public sealed class BackupService : IBackupService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly INoteRepository _repository;
    private readonly IWindowManager _windowManager;
    private readonly ILogger<BackupService> _logger;

    public BackupService(INoteRepository repository, IWindowManager windowManager, ILogger<BackupService> logger)
    {
        _repository = repository;
        _windowManager = windowManager;
        _logger = logger;
    }

    public async Task ExportInteractiveAsync()
    {
        var dlg = new SaveFileDialog
        {
            Filter = Strings.Backup_JsonFilter,
            FileName = $"stickypad-{DateTime.Now:yyyyMMdd-HHmmss}.json",
            Title = Strings.Backup_ExportDialogTitle,
        };
        if (dlg.ShowDialog() != true) return;

        var notes = await _repository.GetAllAsync().ConfigureAwait(true);
        var payload = new BackupPayload(1, DateTime.UtcNow, notes);
        await using var stream = File.Create(dlg.FileName);
        await JsonSerializer.SerializeAsync(stream, payload, JsonOptions).ConfigureAwait(true);
        _logger.LogInformation("Exported {Count} notes to {Path}", notes.Count, dlg.FileName);
    }

    public async Task ExportNotesAsTextAsync()
    {
        var dlg = new SaveFileDialog
        {
            Filter = Strings.Backup_TextExportFilter,
            FileName = $"stickypad-notes-{DateTime.Now:yyyyMMdd-HHmmss}.md",
            Title = Strings.Backup_TextExportDialogTitle,
        };
        if (dlg.ShowDialog() != true) return;

        var notes = await _repository.GetAllAsync().ConfigureAwait(true);
        var sb = new StringBuilder();
        sb.AppendLine("# StickyPad notes");
        sb.AppendLine($"_Exported {DateTime.Now:yyyy-MM-dd HH:mm} · {notes.Count} note(s)_");
        sb.AppendLine();

        foreach (var note in notes)
        {
            var title = string.IsNullOrWhiteSpace(note.Title) ? "(untitled)" : note.Title;
            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine($"## {title}");
            sb.AppendLine();

            var body = (note.PlainText ?? string.Empty).Replace("\r\n", "\n").TrimEnd();
            // PlainText's first line is the derived title — drop it so the title isn't printed twice.
            var lines = body.Split('\n');
            if (lines.Length > 0 && lines[0].Trim() == title)
            {
                body = string.Join("\n", lines.Skip(1)).Trim('\n');
            }
            sb.AppendLine(body.Length == 0 ? "_(empty)_" : body);
            sb.AppendLine();

            if (note.Tags is { Count: > 0 })
            {
                sb.AppendLine("Tags: " + string.Join(" ", note.Tags.Select(t => "#" + t)));
                sb.AppendLine();
            }
            sb.AppendLine($"<sub>Modified {note.ModifiedAt.ToLocalTime():yyyy-MM-dd HH:mm}</sub>");
            sb.AppendLine();
        }

        await File.WriteAllTextAsync(dlg.FileName, sb.ToString()).ConfigureAwait(true);
        _logger.LogInformation("Exported {Count} notes as text to {Path}", notes.Count, dlg.FileName);
    }

    public async Task ExportNotesAsync(IReadOnlyList<Guid> noteIds, ExportFormat format)
    {
        if (noteIds is null || noteIds.Count == 0) return;

        var all = await _repository.GetAllAsync().ConfigureAwait(true);
        var byId = all.ToDictionary(n => n.Id);
        // 선택 순서를 유지하고, 그 사이 삭제된 id 는 조용히 건너뛴다.
        var notes = noteIds.Where(byId.ContainsKey).Select(id => byId[id]).ToList();
        if (notes.Count == 0)
        {
            MessageBox.Show(Strings.Backup_NoNotesToExport, "StickyPad",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        switch (format)
        {
            case ExportFormat.MarkdownFiles: await ExportAsMarkdownFilesAsync(notes).ConfigureAwait(true); break;
            case ExportFormat.Html: await ExportAsHtmlAsync(notes).ConfigureAwait(true); break;
            case ExportFormat.Pdf: await ExportAsPdfAsync(notes).ConfigureAwait(true); break;
        }
    }

    private async Task ExportAsMarkdownFilesAsync(IReadOnlyList<Note> notes)
    {
        var dlg = new OpenFolderDialog { Title = Strings.Backup_ChooseExportFolderTitle };
        if (dlg.ShowDialog() != true) return;
        var folder = dlg.FolderName;

        // 폴더에 이미 있는 파일명을 미리 예약해 둔다 — 같은 이름의 기존 파일을 덮어쓰지 않도록.
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var existing in Directory.EnumerateFiles(folder))
            {
                taken.Add(Path.GetFileName(existing).ToLowerInvariant());
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not enumerate target folder before export: {Folder}", folder);
        }

        var count = 0;
        var failures = new List<string>();
        foreach (var note in notes)
        {
            var baseName = ExportNaming.SafeFileName(note.Title);
            var fileName = ExportNaming.UniqueFileName(baseName, ".md", taken);
            try
            {
                await File.WriteAllTextAsync(Path.Combine(folder, fileName), BuildMarkdown(note)).ConfigureAwait(true);
                count++;
            }
            catch (Exception ex)
            {
                // 한 노트가 실패해도 나머지는 계속 저장하고, 마지막에 실패 목록을 보고한다.
                _logger.LogError(ex, "Failed to write note {Id} to {File}", note.Id, fileName);
                failures.Add(fileName);
            }
        }

        _logger.LogInformation("Exported {Count}/{Total} notes as .md files to {Folder}", count, notes.Count, folder);
        if (failures.Count == 0)
        {
            MessageBox.Show(string.Format(Strings.Backup_MarkdownFilesExportedFormat, count, folder), "StickyPad",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            var list = string.Join(", ", failures.Take(5)) + (failures.Count > 5 ? " …" : "");
            MessageBox.Show(
                string.Format(Strings.Backup_MarkdownFilesPartialFailureFormat, count, failures.Count, list),
                "StickyPad", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task ExportAsHtmlAsync(IReadOnlyList<Note> notes)
    {
        var dlg = new SaveFileDialog
        {
            Filter = Strings.Export_HtmlFilter,
            FileName = $"stickypad-notes-{DateTime.Now:yyyyMMdd-HHmmss}.html",
            Title = Strings.Export_HtmlDialogTitle,
        };
        if (dlg.ShowDialog() != true) return;

        var html = HtmlRenderer.RenderDocument(notes, Strings.Export_DocumentTitle);
        try
        {
            await File.WriteAllTextAsync(dlg.FileName, html).ConfigureAwait(true);
            _logger.LogInformation("Exported {Count} notes as HTML to {Path}", notes.Count, dlg.FileName);
            MessageBox.Show(string.Format(Strings.Backup_HtmlExportedFormat, notes.Count, dlg.FileName), "StickyPad",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HTML export failed");
            MessageBox.Show(string.Format(Strings.Export_HtmlFailedFormat, ex.Message), "StickyPad",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task ExportAsPdfAsync(IReadOnlyList<Note> notes)
    {
        var dlg = new SaveFileDialog
        {
            Filter = Strings.Export_PdfFilter,
            FileName = $"stickypad-notes-{DateTime.Now:yyyyMMdd-HHmmss}.pdf",
            Title = Strings.Export_PdfDialogTitle,
        };
        if (dlg.ShowDialog() != true) return;

        var html = HtmlRenderer.RenderDocument(notes, Strings.Export_DocumentTitle);
        try
        {
            await PdfRenderer.RenderAsync(html, dlg.FileName).ConfigureAwait(true);
            _logger.LogInformation("Exported {Count} notes as PDF to {Path}", notes.Count, dlg.FileName);
            MessageBox.Show(string.Format(Strings.Backup_PdfExportedFormat, notes.Count, dlg.FileName), "StickyPad",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PDF export failed");
            MessageBox.Show(
                string.Format(Strings.Export_PdfFailedFormat, ex.Message),
                "StickyPad", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    public async Task ExportSingleNoteAsync(Note note, ExportFormat format)
    {
        var safe = ExportNaming.SafeFileName(note.Title);
        var docTitle = string.IsNullOrWhiteSpace(note.Title) ? Strings.Export_DocumentTitle : note.Title;

        switch (format)
        {
            case ExportFormat.Html:
            {
                var dlg = new SaveFileDialog
                {
                    Filter = Strings.Export_HtmlFilter,
                    FileName = safe + ".html",
                    Title = Strings.Export_HtmlDialogTitle,
                };
                if (dlg.ShowDialog() != true) return;

                try
                {
                    await File.WriteAllTextAsync(dlg.FileName, HtmlRenderer.RenderDocument(new[] { note }, docTitle))
                        .ConfigureAwait(true);
                    _logger.LogInformation("Exported note {Id} as HTML to {Path}", note.Id, dlg.FileName);
                    MessageBox.Show(string.Format(Strings.Export_HtmlSavedFormat, dlg.FileName), "StickyPad",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Single-note HTML export failed");
                    MessageBox.Show(string.Format(Strings.Export_HtmlFailedFormat, ex.Message), "StickyPad",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                break;
            }
            case ExportFormat.Pdf:
            {
                var dlg = new SaveFileDialog
                {
                    Filter = Strings.Export_PdfFilter,
                    FileName = safe + ".pdf",
                    Title = Strings.Export_PdfDialogTitle,
                };
                if (dlg.ShowDialog() != true) return;

                try
                {
                    await PdfRenderer.RenderAsync(HtmlRenderer.RenderDocument(new[] { note }, docTitle), dlg.FileName)
                        .ConfigureAwait(true);
                    _logger.LogInformation("Exported note {Id} as PDF to {Path}", note.Id, dlg.FileName);
                    MessageBox.Show(string.Format(Strings.Export_PdfSavedFormat, dlg.FileName), "StickyPad",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Single-note PDF export failed");
                    MessageBox.Show(
                        string.Format(Strings.Export_PdfFailedFormat, ex.Message),
                        "StickyPad", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                break;
            }
            case ExportFormat.MarkdownFiles:
            {
                var dlg = new SaveFileDialog
                {
                    Filter = Strings.Export_MarkdownFilter,
                    FileName = safe + ".md",
                    Title = Strings.Export_MarkdownDialogTitle,
                };
                if (dlg.ShowDialog() != true) return;

                try
                {
                    await File.WriteAllTextAsync(dlg.FileName, BuildMarkdown(note)).ConfigureAwait(true);
                    _logger.LogInformation("Exported note {Id} as Markdown to {Path}", note.Id, dlg.FileName);
                    MessageBox.Show(string.Format(Strings.Export_MarkdownSavedFormat, dlg.FileName), "StickyPad",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Single-note Markdown export failed");
                    MessageBox.Show(string.Format(Strings.Export_MarkdownFailedFormat, ex.Message), "StickyPad",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                break;
            }
        }
    }

    public async Task PrintNoteAsync(Note note)
    {
        var docTitle = string.IsNullOrWhiteSpace(note.Title) ? Strings.Export_DocumentTitle : note.Title;
        var html = HtmlRenderer.RenderDocument(new[] { note }, docTitle);
        var tmp = Path.Combine(Path.GetTempPath(), $"stickypad-print-{Guid.NewGuid():N}.html");
        await File.WriteAllTextAsync(tmp, html).ConfigureAwait(true);

        var udf = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "StickyPad", "WebView2-export");
        Directory.CreateDirectory(udf);

        var web = new WebView2 { CreationProperties = new CoreWebView2CreationProperties { UserDataFolder = udf } };
        var host = new Window
        {
            Title = Strings.Print_WindowTitle,
            Width = 860,
            Height = 1000,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = web,
        };
        host.Closed += (_, _) => { try { web.Dispose(); } catch { } try { File.Delete(tmp); } catch { } };
        host.Show();
        try
        {
            await web.EnsureCoreWebView2Async().ConfigureAwait(true);
            var s = web.CoreWebView2.Settings;
            s.IsScriptEnabled = false; s.AreHostObjectsAllowed = false; s.IsWebMessageEnabled = false;
            var nav = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            web.NavigationCompleted += (_, e) => nav.TrySetResult(e.IsSuccess);
            web.CoreWebView2.Navigate(new Uri(tmp).AbsoluteUri);
            await nav.Task.ConfigureAwait(true);
            web.CoreWebView2.ShowPrintUI(CoreWebView2PrintDialogKind.Browser);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Print failed");
            MessageBox.Show(string.Format(Strings.Print_PrepareFailedFormat, ex.Message), "StickyPad",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            host.Close();
        }
    }

    /// 노트 1개를 YAML 프런트매터 + 본문의 .md 텍스트로 만든다.
    private static string BuildMarkdown(Note note)
    {
        var sb = new StringBuilder();
        var title = string.IsNullOrWhiteSpace(note.Title) ? Strings.NoteList_Untitled : note.Title;

        sb.Append("---\n");
        sb.Append("title: ").Append(YamlScalar(title)).Append('\n');
        if (note.Tags is { Count: > 0 })
        {
            sb.Append("tags: [").Append(string.Join(", ", note.Tags.Select(YamlScalar))).Append("]\n");
        }
        sb.Append("color: ").Append(note.Color).Append('\n');
        sb.Append("created: ").Append(note.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm")).Append('\n');
        sb.Append("modified: ").Append(note.ModifiedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm")).Append('\n');
        sb.Append("---\n\n");

        var body = note.Format switch
        {
            // Markdown/HTML 노트는 원본 소스를 그대로(HTML 은 .md 안에서도 유효).
            NoteContentFormat.Markdown or NoteContentFormat.Html => note.Content ?? string.Empty,
            // PlainText/RichTextXaml 은 사람이 읽는 PlainText 투영을 쓴다(XAML 원본을 넣지 않는다).
            _ => note.PlainText ?? string.Empty,
        };
        sb.Append(body.Replace("\r\n", "\n").TrimEnd()).Append('\n');
        return sb.ToString();
    }

    /// YAML 값에 특수문자가 있으면 큰따옴표로 감싸 안전하게 만든다.
    private static string YamlScalar(string value)
    {
        value = value.Replace("\r", " ").Replace("\n", " ");
        if (value.Length == 0) return "\"\"";

        var needsQuote = value[0] == ' ' || value[^1] == ' ' ||
            value.IndexOfAny(YamlSpecials) >= 0;
        if (!needsQuote) return value;
        return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }

    private static readonly char[] YamlSpecials =
        { ':', '#', '"', '\'', '[', ']', '{', '}', ',', '&', '*', '!', '|', '>', '%', '@', '`' };

    public async Task ImportInteractiveAsync()
    {
        var dlg = new OpenFileDialog
        {
            Filter = Strings.Backup_JsonImportFilter,
            Title = Strings.Backup_ImportDialogTitle,
        };
        if (dlg.ShowDialog() != true) return;

        BackupPayload? payload;
        try
        {
            await using var stream = File.OpenRead(dlg.FileName);
            payload = await JsonSerializer.DeserializeAsync<BackupPayload>(stream).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Import parse failure");
            MessageBox.Show(string.Format(Strings.Backup_ReadFailedFormat, ex.Message), "StickyPad",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (payload?.Notes is null || payload.Notes.Count == 0)
        {
            MessageBox.Show(Strings.Backup_NoNotesInFile, "StickyPad",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show(
            string.Format(Strings.Backup_ImportConfirmFormat, payload.Notes.Count),
            "StickyPad",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.OK) return;

        foreach (var note in payload.Notes)
        {
            await _repository.UpsertAsync(SanitizeImportedNote(note)).ConfigureAwait(true);
        }
        await _windowManager.ReloadAsync().ConfigureAwait(true);
        _logger.LogInformation("Imported {Count} notes from {Path}", payload.Notes.Count, dlg.FileName);
    }

    public async Task ExportVaultAsync()
    {
        var dlg = new OpenFolderDialog { Title = Strings.Vault_ChooseExportFolder };
        if (dlg.ShowDialog() != true) return;
        var folder = dlg.FolderName;

        var notes = await _repository.GetAllAsync().ConfigureAwait(true);

        // 폴더에 이미 있는 파일명을 미리 예약해 둔다 — 같은 이름의 기존 파일을 덮어쓰지 않도록.
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var existing in Directory.EnumerateFiles(folder))
            {
                taken.Add(Path.GetFileName(existing).ToLowerInvariant());
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not enumerate target folder before vault export: {Folder}", folder);
        }

        var count = 0;
        var failures = new List<string>();
        foreach (var note in notes)
        {
            var baseName = ExportNaming.SafeFileName(note.Title);
            var fileName = ExportNaming.UniqueFileName(baseName, ".md", taken);
            try
            {
                await File.WriteAllTextAsync(Path.Combine(folder, fileName), VaultMarkdown.ToMarkdown(note))
                    .ConfigureAwait(true);
                count++;
            }
            catch (Exception ex)
            {
                // 한 노트가 실패해도 나머지는 계속 저장하고, 마지막에 실패 목록을 보고한다.
                _logger.LogError(ex, "Failed to write vault note {Id} to {File}", note.Id, fileName);
                failures.Add(fileName);
            }
        }

        _logger.LogInformation("Exported {Count}/{Total} notes to vault {Folder}", count, notes.Count, folder);
        if (failures.Count == 0)
        {
            MessageBox.Show(string.Format(Strings.Vault_ExportedFormat, count, folder), "StickyPad",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            var list = string.Join(", ", failures.Take(5)) + (failures.Count > 5 ? " …" : "");
            MessageBox.Show(
                string.Format(Strings.Backup_MarkdownFilesPartialFailureFormat, count, failures.Count, list),
                "StickyPad", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    public async Task ImportVaultAsync()
    {
        var dlg = new OpenFolderDialog { Title = Strings.Vault_ChooseImportFolder };
        if (dlg.ShowDialog() != true) return;
        var folder = dlg.FolderName;

        List<string> files;
        try
        {
            files = Directory.EnumerateFiles(folder, "*.md", SearchOption.TopDirectoryOnly).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not enumerate vault folder: {Folder}", folder);
            return;
        }

        if (files.Count == 0)
        {
            MessageBox.Show(Strings.Vault_NoMarkdownFiles, "StickyPad",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show(
            string.Format(Strings.Vault_ImportConfirmFormat, files.Count),
            "StickyPad",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.OK) return;

        var count = 0;
        var failures = new List<string>();
        foreach (var file in files)
        {
            try
            {
                var text = await File.ReadAllTextAsync(file).ConfigureAwait(true);
                var note = SanitizeImportedNote(VaultMarkdown.FromMarkdown(text, Path.GetFileNameWithoutExtension(file)));
                await _repository.UpsertAsync(note).ConfigureAwait(true);
                count++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to import vault file {File}", file);
                failures.Add(Path.GetFileName(file));
            }
        }

        await _windowManager.ReloadAsync().ConfigureAwait(true);
        _logger.LogInformation("Imported {Count}/{Total} notes from vault {Folder}", count, files.Count, folder);

        if (failures.Count == 0)
        {
            MessageBox.Show(string.Format(Strings.Vault_ImportedFormat, count), "StickyPad",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            var list = string.Join(", ", failures.Take(5)) + (failures.Count > 5 ? " …" : "");
            MessageBox.Show(
                string.Format(Strings.Backup_MarkdownFilesPartialFailureFormat, count, failures.Count, list),
                "StickyPad", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// 신뢰할 수 없는 백업 파일에서 온 노트의 위험 필드를 무해화한다(보안).
    /// - RichTextXaml 은 창이 열릴 때 비제한 XAML 파서(TextRange.Load)로 ObjectDataProvider 같은
    ///   가젯이 실행돼 임의 코드 실행이 가능하므로 순수 텍스트로 강등한다.
    /// - LinkedFilePath 는 편집 시 그 경로에 노트 내용을 조용히 써서 임의 파일을 덮어쓰는 통로가
    ///   되므로 제거한다(가져온 노트는 일반 노트가 된다 — 필요하면 파일을 다시 열어 연동).
    internal static Note SanitizeImportedNote(Note note)
    {
        note.LinkedFilePath = null;
        note.LinkedFileSyncedUtc = null;
        if (note.Format == NoteContentFormat.RichTextXaml)
        {
            note.Content = note.PlainText ?? string.Empty;
            note.Format = NoteContentFormat.PlainText;
        }
        return note;
    }

    private sealed record BackupPayload(int Version, DateTime ExportedAt, System.Collections.Generic.IReadOnlyList<Note> Notes);
}
