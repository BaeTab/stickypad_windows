using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using StickyPad.Models;

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
            Filter = "StickyPad backup (*.json)|*.json",
            FileName = $"stickypad-{DateTime.Now:yyyyMMdd-HHmmss}.json",
            Title = "Export StickyPad backup",
        };
        if (dlg.ShowDialog() != true) return;

        var notes = await _repository.GetAllAsync().ConfigureAwait(true);
        var payload = new BackupPayload(1, DateTime.UtcNow, notes);
        await using var stream = File.Create(dlg.FileName);
        await JsonSerializer.SerializeAsync(stream, payload, JsonOptions).ConfigureAwait(true);
        _logger.LogInformation("Exported {Count} notes to {Path}", notes.Count, dlg.FileName);
    }

    public async Task ImportInteractiveAsync()
    {
        var dlg = new OpenFileDialog
        {
            Filter = "StickyPad backup (*.json)|*.json|All files (*.*)|*.*",
            Title = "Import StickyPad backup",
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
            MessageBox.Show($"Could not read backup:\n{ex.Message}", "StickyPad",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (payload?.Notes is null || payload.Notes.Count == 0)
        {
            MessageBox.Show("Backup contains no notes.", "StickyPad",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show(
            $"Import {payload.Notes.Count} note(s)? Existing notes with the same id will be overwritten.",
            "StickyPad",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.OK) return;

        foreach (var note in payload.Notes)
        {
            await _repository.UpsertAsync(note).ConfigureAwait(true);
        }
        await _windowManager.ReloadAsync().ConfigureAwait(true);
        _logger.LogInformation("Imported {Count} notes from {Path}", payload.Notes.Count, dlg.FileName);
    }

    private sealed record BackupPayload(int Version, DateTime ExportedAt, System.Collections.Generic.IReadOnlyList<Note> Notes);
}
