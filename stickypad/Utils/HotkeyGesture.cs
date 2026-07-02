using System;
using System.Text;
using System.Windows.Input;

namespace StickyPad.Utils;

/// Parses and formats hotkey gesture strings like "Ctrl+Shift+N" so the same rules
/// are shared by HotkeyService (registration) and the settings UI (capture/validation).
public static class HotkeyGesture
{
    /// True only for gestures with at least one modifier and a resolvable key —
    /// global hotkeys without a modifier would hijack ordinary typing.
    public static bool TryParse(string? gesture, out Key key, out ModifierKeys modifiers)
    {
        key = Key.None;
        modifiers = ModifierKeys.None;
        if (string.IsNullOrWhiteSpace(gesture)) return false;

        var parts = gesture.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2) return false;

        for (var i = 0; i < parts.Length - 1; i++)
        {
            switch (parts[i].ToLowerInvariant())
            {
                case "ctrl":
                case "control": modifiers |= ModifierKeys.Control; break;
                case "shift": modifiers |= ModifierKeys.Shift; break;
                case "alt": modifiers |= ModifierKeys.Alt; break;
                case "win":
                case "windows": modifiers |= ModifierKeys.Windows; break;
                default: return false;
            }
        }

        if (!TryParseKey(parts[^1], out key)) return false;
        return modifiers != ModifierKeys.None && key != Key.None;
    }

    private static bool TryParseKey(string token, out Key key)
    {
        key = Key.None;
        if (string.IsNullOrEmpty(token)) return false;

        // single digit -> D0..D9 (Enum.TryParse("1") fails otherwise)
        if (token.Length == 1 && token[0] >= '0' && token[0] <= '9')
        {
            key = Key.D0 + (token[0] - '0');
            return true;
        }
        return Enum.TryParse(token, ignoreCase: true, out key) && key != Key.None;
    }

    /// Renders a Key + modifiers back to the canonical "Ctrl+Shift+N" form.
    public static string Format(Key key, ModifierKeys modifiers)
    {
        var sb = new StringBuilder();
        if (modifiers.HasFlag(ModifierKeys.Control)) sb.Append("Ctrl+");
        if (modifiers.HasFlag(ModifierKeys.Shift)) sb.Append("Shift+");
        if (modifiers.HasFlag(ModifierKeys.Alt)) sb.Append("Alt+");
        if (modifiers.HasFlag(ModifierKeys.Windows)) sb.Append("Win+");
        sb.Append(KeyLabel(key));
        return sb.ToString();
    }

    private static string KeyLabel(Key key) => key switch
    {
        >= Key.A and <= Key.Z => key.ToString(),
        >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => "NumPad" + (key - Key.NumPad0),
        _ => key.ToString(),
    };
}
