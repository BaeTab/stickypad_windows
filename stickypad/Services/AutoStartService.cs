using System;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace StickyPad.Services;

public sealed class AutoStartService : IAutoStartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "StickyPad";

    private readonly ILogger<AutoStartService> _logger;

    public AutoStartService(ILogger<AutoStartService> logger)
    {
        _logger = logger;
    }

    public bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
                return key?.GetValue(ValueName) is string;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read auto-start registry");
                return false;
            }
        }
    }

    public void SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (key is null) return;
            if (enabled)
            {
                var exe = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(exe)) return;
                key.SetValue(ValueName, $"\"{exe}\"");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update auto-start (enabled={Enabled})", enabled);
        }
    }
}
