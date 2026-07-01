using System.Collections.Generic;
using System.Windows.Media;

namespace StickyPad.Models;

public sealed record NoteTheme(
    NoteColor Color,
    string DisplayName,
    Color Background,
    Color Header,
    Color Border,
    Color Foreground,
    Color SubtleForeground);

public static class NotePalette
{
    private static readonly Dictionary<NoteColor, NoteTheme> Themes = new()
    {
        [NoteColor.Yellow] = new(
            NoteColor.Yellow, "Yellow",
            Background: Color.FromRgb(0xFE, 0xF3, 0xB0),
            Header: Color.FromRgb(0xF2, 0xE2, 0x8E),
            Border: Color.FromRgb(0xC9, 0xB6, 0x6A),
            Foreground: Color.FromRgb(0x2A, 0x24, 0x10),
            SubtleForeground: Color.FromRgb(0x6B, 0x5C, 0x2C)),

        [NoteColor.Pink] = new(
            NoteColor.Pink, "Pink",
            Background: Color.FromRgb(0xFB, 0xD0, 0xE0),
            Header: Color.FromRgb(0xEF, 0xAF, 0xC8),
            Border: Color.FromRgb(0xC4, 0x82, 0xA1),
            Foreground: Color.FromRgb(0x33, 0x16, 0x22),
            SubtleForeground: Color.FromRgb(0x6F, 0x3F, 0x53)),

        [NoteColor.Blue] = new(
            NoteColor.Blue, "Blue",
            Background: Color.FromRgb(0xCC, 0xE4, 0xFF),
            Header: Color.FromRgb(0xA4, 0xCB, 0xF7),
            Border: Color.FromRgb(0x6A, 0x9B, 0xCE),
            Foreground: Color.FromRgb(0x10, 0x20, 0x35),
            SubtleForeground: Color.FromRgb(0x37, 0x56, 0x80)),

        [NoteColor.Green] = new(
            NoteColor.Green, "Green",
            Background: Color.FromRgb(0xCC, 0xEA, 0xC8),
            Header: Color.FromRgb(0xA8, 0xD7, 0xA1),
            Border: Color.FromRgb(0x73, 0xA9, 0x6B),
            Foreground: Color.FromRgb(0x12, 0x2A, 0x14),
            SubtleForeground: Color.FromRgb(0x3F, 0x66, 0x3D)),

        [NoteColor.Purple] = new(
            NoteColor.Purple, "Purple",
            Background: Color.FromRgb(0xE0, 0xD3, 0xF7),
            Header: Color.FromRgb(0xC1, 0xAE, 0xE6),
            Border: Color.FromRgb(0x8C, 0x73, 0xBE),
            Foreground: Color.FromRgb(0x24, 0x16, 0x3A),
            SubtleForeground: Color.FromRgb(0x55, 0x42, 0x7E)),

        [NoteColor.Gray] = new(
            NoteColor.Gray, "Gray",
            Background: Color.FromRgb(0x37, 0x3A, 0x40),
            Header: Color.FromRgb(0x4A, 0x4E, 0x57),
            Border: Color.FromRgb(0x22, 0x24, 0x29),
            Foreground: Color.FromRgb(0xEC, 0xEE, 0xF2),
            SubtleForeground: Color.FromRgb(0xB1, 0xB6, 0xC1)),
    };

    public static NoteTheme For(NoteColor color) =>
        Themes.TryGetValue(color, out var theme) ? theme : Themes[NoteColor.Yellow];

    public static IReadOnlyList<NoteTheme> All { get; } = new List<NoteTheme>
    {
        Themes[NoteColor.Yellow],
        Themes[NoteColor.Pink],
        Themes[NoteColor.Blue],
        Themes[NoteColor.Green],
        Themes[NoteColor.Purple],
        Themes[NoteColor.Gray],
    };
}
