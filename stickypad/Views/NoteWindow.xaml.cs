using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;
using StickyPad.Models;
using StickyPad.Services;
using StickyPad.Utils;
using StickyPad.ViewModels;

namespace StickyPad.Views;

public partial class NoteWindow : Window
{
    private static readonly DependencyProperty IsToolbarVisibleProperty =
        DependencyProperty.Register(nameof(IsToolbarVisible), typeof(bool), typeof(NoteWindow),
            new PropertyMetadata(false));

    private static readonly Color CodeBackground = Color.FromRgb(0xEF, 0xEF, 0xEF);
    private static readonly Color CodeBlockBackground = Color.FromRgb(0xF6, 0xF6, 0xF1);

    private readonly NoteViewModel _viewModel;
    private readonly Action<NoteWindow> _onHidden;
    private readonly Func<NoteWindow> _onNewRequested;
    private readonly Action _onListRequested;
    private readonly Func<string, bool> _onLinkActivated;
    private bool _suppressEditorSync;
    private bool _suppressToolbarSync;
    private bool _allowClose;

    public NoteViewModel ViewModel => _viewModel;
    public IReadOnlyList<double> FontSizeChoices => NoteViewModel.FontSizeCatalog;

    public bool IsToolbarVisible
    {
        get => (bool)GetValue(IsToolbarVisibleProperty);
        private set => SetValue(IsToolbarVisibleProperty, value);
    }

    public NoteWindow(
        NoteViewModel viewModel,
        Action<NoteWindow> onHidden,
        Func<NoteWindow> onNewRequested,
        Action onListRequested,
        Func<string, bool> onLinkActivated)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _onHidden = onHidden;
        _onNewRequested = onNewRequested;
        _onListRequested = onListRequested;
        _onLinkActivated = onLinkActivated;

        Icon = IconFactory.CreateAppIcon();

        DataContext = viewModel;
        Left = viewModel.X;
        Top = viewModel.Y;
        Width = viewModel.Width;
        Height = viewModel.Height;

        Loaded += OnLoaded;
        LocationChanged += (_, _) => SyncGeometry();
        SizeChanged += (_, _) => SyncGeometry();
        Closing += OnClosing;

        Activated += (_, _) => UpdateToolbarVisibility();
        Deactivated += (_, _) => UpdateToolbarVisibility();
        MouseEnter += (_, _) => UpdateToolbarVisibility();
        MouseLeave += (_, _) => UpdateToolbarVisibility();
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(NoteViewModel.IsPreviewMode))
            {
                UpdateToolbarVisibility();
            }
        };

        InputBindings.Add(new KeyBinding(
            new RelayCommandImpl(_ => ToggleStrikethrough()),
            new KeyGesture(Key.X, ModifierKeys.Control | ModifierKeys.Shift)));
        InputBindings.Add(new KeyBinding(
            new RelayCommandImpl(_ => _viewModel.IsPreviewMode = !_viewModel.IsPreviewMode),
            new KeyGesture(Key.E, ModifierKeys.Control)));
        InputBindings.Add(new KeyBinding(
            new RelayCommandImpl(_ => ToggleInlineCode()),
            new KeyGesture(Key.OemTilde, ModifierKeys.Control)));
    }

    public void RequestClose()
    {
        _allowClose = true;
        Close();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        LoadEditorContent();
        DecorateLinks();
        UpdateToolbarVisibility();
        SyncToolbarFromSelection();
        Editor.Focus();
    }

    private void LoadEditorContent()
    {
        _suppressEditorSync = true;
        try
        {
            Editor.Document.Blocks.Clear();
            if (string.IsNullOrEmpty(_viewModel.Content))
            {
                Editor.Document.Blocks.Add(new Paragraph(new Run(string.Empty)));
                return;
            }

            if (_viewModel.Format == NoteContentFormat.RichTextXaml)
            {
                try
                {
                    var range = new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd);
                    using var ms = new MemoryStream(Encoding.UTF8.GetBytes(_viewModel.Content));
                    range.Load(ms, DataFormats.Xaml);
                    return;
                }
                catch
                {
                    // Fall through to plain-text recovery below.
                }
            }

            Editor.Document.Blocks.Clear();
            Editor.Document.Blocks.Add(new Paragraph(new Run(_viewModel.Content)));
        }
        finally
        {
            _suppressEditorSync = false;
        }
    }

    private void Editor_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressEditorSync) return;

        // Re-wrap freshly-typed [[Title]] tokens as Hyperlinks before serializing — keeps the persisted
        // XAML self-contained so links work after a restart.
        DecorateLinks();

        var range = new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd);
        using var ms = new MemoryStream();
        range.Save(ms, DataFormats.Xaml);
        var xaml = Encoding.UTF8.GetString(ms.ToArray());
        _viewModel.UpdateContent(xaml, NoteContentFormat.RichTextXaml);
    }

    private void Editor_OnSelectionChanged(object sender, RoutedEventArgs e) => SyncToolbarFromSelection();

    private void SyncToolbarFromSelection()
    {
        if (Editor.Selection is null) return;
        _suppressToolbarSync = true;
        try
        {
            BoldToggle.IsChecked = IsSelectionProperty(TextElement.FontWeightProperty, FontWeights.Bold);
            ItalicToggle.IsChecked = IsSelectionProperty(TextElement.FontStyleProperty, FontStyles.Italic);
            UnderlineToggle.IsChecked = SelectionHasDecoration(TextDecorations.Underline);
            StrikeToggle.IsChecked = SelectionHasDecoration(TextDecorations.Strikethrough);

            var alignment = Editor.Selection.GetPropertyValue(Block.TextAlignmentProperty);
            AlignLeftToggle.IsChecked = alignment is TextAlignment.Left;
            AlignCenterToggle.IsChecked = alignment is TextAlignment.Center;
            AlignRightToggle.IsChecked = alignment is TextAlignment.Right;

            BulletsToggle.IsChecked = HasListMarker(TextMarkerStyle.Disc);
            NumberingToggle.IsChecked = HasListMarker(TextMarkerStyle.Decimal);

            var size = Editor.Selection.GetPropertyValue(TextElement.FontSizeProperty);
            FontSizeCombo.SelectedItem = size is double d ? NearestFontSize(d) : (object?)null;
        }
        finally
        {
            _suppressToolbarSync = false;
        }
    }

    private bool IsSelectionProperty(DependencyProperty property, object expected)
    {
        var value = Editor.Selection.GetPropertyValue(property);
        return value is not null && value.Equals(expected);
    }

    private bool SelectionHasDecoration(TextDecorationCollection decoration)
    {
        var value = Editor.Selection.GetPropertyValue(Inline.TextDecorationsProperty);
        if (value is not TextDecorationCollection current) return false;
        var target = decoration.First().Location;
        return current.Any(d => d.Location == target);
    }

    private bool HasListMarker(TextMarkerStyle marker)
    {
        var pointer = Editor.Selection.Start;
        var listItem = pointer.Paragraph?.Parent as ListItem;
        return listItem?.Parent is List list && list.MarkerStyle == marker;
    }

    private static double NearestFontSize(double size)
    {
        double best = NoteViewModel.FontSizeCatalog[0];
        var bestDiff = Math.Abs(size - best);
        foreach (var candidate in NoteViewModel.FontSizeCatalog)
        {
            var diff = Math.Abs(candidate - size);
            if (diff < bestDiff)
            {
                best = candidate;
                bestDiff = diff;
            }
        }
        return best;
    }

    private void SyncGeometry()
    {
        if (double.IsNaN(Left) || double.IsNaN(Top)) return;
        _viewModel.X = Left;
        _viewModel.Y = Top;
        _viewModel.Width = ActualWidth;
        _viewModel.Height = ActualHeight;
    }

    private void UpdateToolbarVisibility()
    {
        IsToolbarVisible = (IsActive || IsMouseOver) && !_viewModel.IsPreviewMode;
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        try { await _viewModel.FlushAsync().ConfigureAwait(true); }
        catch { /* logged in VM */ }

        if (_allowClose || _viewModel.IsEmpty)
        {
            // Empty notes really close (and the manager will delete them); explicit deletes also flow through.
            _onHidden(this);
            return;
        }

        // Spec: closing the X with content keeps it alive via the tray — we hide instead of dispose.
        e.Cancel = true;
        Hide();
        _onHidden(this);
    }

    private void HeaderBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && e.ClickCount == 1)
        {
            try { DragMove(); } catch (InvalidOperationException) { }
        }
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e) => Close();

    private void NewButton_OnClick(object sender, RoutedEventArgs e)
    {
        var window = _onNewRequested();
        window.Show();
        window.Activate();
        window.Editor.Focus();
    }

    private void ListButton_OnClick(object sender, RoutedEventArgs e) => _onListRequested();

    private async void DeleteButton_OnClick(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(
            this,
            "Delete this note? This cannot be undone.",
            "StickyPad",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning,
            MessageBoxResult.Cancel);
        if (confirm != MessageBoxResult.OK) return;
        await _viewModel.DeleteCommand.ExecuteAsync(null).ConfigureAwait(true);
    }

    private void ColorSwatch_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is NoteColor color)
        {
            _viewModel.SetColorCommand.Execute(color);
            ColorPickerToggle.IsChecked = false;
        }
    }

    private void BoldToggle_OnClick(object sender, RoutedEventArgs e)
    {
        if (_suppressToolbarSync) return;
        EditingCommands.ToggleBold.Execute(null, Editor);
        Editor.Focus();
    }

    private void ItalicToggle_OnClick(object sender, RoutedEventArgs e)
    {
        if (_suppressToolbarSync) return;
        EditingCommands.ToggleItalic.Execute(null, Editor);
        Editor.Focus();
    }

    private void UnderlineToggle_OnClick(object sender, RoutedEventArgs e)
    {
        if (_suppressToolbarSync) return;
        EditingCommands.ToggleUnderline.Execute(null, Editor);
        Editor.Focus();
    }

    private void StrikeToggle_OnClick(object sender, RoutedEventArgs e)
    {
        if (_suppressToolbarSync) return;
        ToggleStrikethrough();
    }

    private void ToggleStrikethrough()
    {
        if (Editor.Selection.IsEmpty) return;
        var current = Editor.Selection.GetPropertyValue(Inline.TextDecorationsProperty)
            as TextDecorationCollection;
        var hasStrike = current?.Any(d => d.Location == TextDecorations.Strikethrough.First().Location) == true;
        Editor.Selection.ApplyPropertyValue(Inline.TextDecorationsProperty,
            hasStrike ? null : TextDecorations.Strikethrough);
        Editor.Focus();
    }

    private void AlignLeftToggle_OnClick(object sender, RoutedEventArgs e)
    {
        if (_suppressToolbarSync) return;
        EditingCommands.AlignLeft.Execute(null, Editor);
        Editor.Focus();
    }

    private void AlignCenterToggle_OnClick(object sender, RoutedEventArgs e)
    {
        if (_suppressToolbarSync) return;
        EditingCommands.AlignCenter.Execute(null, Editor);
        Editor.Focus();
    }

    private void AlignRightToggle_OnClick(object sender, RoutedEventArgs e)
    {
        if (_suppressToolbarSync) return;
        EditingCommands.AlignRight.Execute(null, Editor);
        Editor.Focus();
    }

    private void BulletsToggle_OnClick(object sender, RoutedEventArgs e)
    {
        if (_suppressToolbarSync) return;
        EditingCommands.ToggleBullets.Execute(null, Editor);
        Editor.Focus();
    }

    private void NumberingToggle_OnClick(object sender, RoutedEventArgs e)
    {
        if (_suppressToolbarSync) return;
        EditingCommands.ToggleNumbering.Execute(null, Editor);
        Editor.Focus();
    }

    private void TaskItem_OnClick(object sender, RoutedEventArgs e)
    {
        InsertTaskAtCaret();
        Editor.Focus();
    }

    private void InlineCode_OnClick(object sender, RoutedEventArgs e) => ToggleInlineCode();

    private void ToggleInlineCode()
    {
        if (Editor.Selection.IsEmpty) return;
        var monospace = (FontFamily)new FontFamilyConverter().ConvertFromString("Cascadia Mono, Consolas, monospace")!;
        var current = Editor.Selection.GetPropertyValue(TextElement.FontFamilyProperty) as FontFamily;
        var isCode = current is not null && string.Equals(current.Source, monospace.Source, StringComparison.OrdinalIgnoreCase);
        if (isCode)
        {
            Editor.Selection.ClearAllProperties();
        }
        else
        {
            Editor.Selection.ApplyPropertyValue(TextElement.FontFamilyProperty, monospace);
            var bg = new SolidColorBrush(CodeBackground);
            bg.Freeze();
            Editor.Selection.ApplyPropertyValue(TextElement.BackgroundProperty, bg);
        }
        Editor.Focus();
    }

    private void CodeBlock_OnClick(object sender, RoutedEventArgs e)
    {
        var paragraph = Editor.Selection.Start.Paragraph ?? Editor.CaretPosition.Paragraph;
        if (paragraph is null) return;

        var monospace = (FontFamily)new FontFamilyConverter().ConvertFromString("Cascadia Mono, Consolas, monospace")!;
        paragraph.FontFamily = monospace;
        var bg = new SolidColorBrush(CodeBlockBackground);
        bg.Freeze();
        paragraph.Background = bg;
        paragraph.Padding = new Thickness(8, 6, 8, 6);
        paragraph.Margin = new Thickness(0, 4, 0, 8);
        Editor.Focus();
    }

    private void ClearFormatting_OnClick(object sender, RoutedEventArgs e)
    {
        if (Editor.Selection.IsEmpty) Editor.SelectAll();
        Editor.Selection.ClearAllProperties();
        Editor.Focus();
        SyncToolbarFromSelection();
    }

    private void FontSizeCombo_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressToolbarSync) return;
        if (FontSizeCombo.SelectedItem is double size && Editor.Selection is not null)
        {
            if (Editor.Selection.IsEmpty)
            {
                _viewModel.EditorFontSize = size;
            }
            else
            {
                Editor.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, size);
            }
            Editor.Focus();
        }
    }

    private void InsertTaskAtCaret()
    {
        var checkbox = new CheckBox
        {
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Focusable = false,
        };
        checkbox.Checked += (_, _) => _viewModel.UpdateContent(SerializeDocument(), NoteContentFormat.RichTextXaml);
        checkbox.Unchecked += (_, _) => _viewModel.UpdateContent(SerializeDocument(), NoteContentFormat.RichTextXaml);

        var inline = new InlineUIContainer(checkbox, Editor.CaretPosition);
        Editor.CaretPosition = inline.ElementEnd;
    }

    private string SerializeDocument()
    {
        var range = new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd);
        using var ms = new MemoryStream();
        range.Save(ms, DataFormats.Xaml);
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private void DecorateLinks()
    {
        // Wrap raw `[[Title]]` text in plain Runs into Hyperlinks. Skips runs already inside a Hyperlink so we
        // don't keep nesting on every keystroke. Suppresses re-entrant TextChanged while the tree is rewritten.
        var snapshot = CollectRuns(Editor.Document).ToList();
        if (snapshot.Count == 0) return;

        var wasSuppressed = _suppressEditorSync;
        _suppressEditorSync = true;
        try
        {
            foreach (var run in snapshot)
            {
                DecorateRun(run);
            }
        }
        finally
        {
            _suppressEditorSync = wasSuppressed;
        }
    }

    private void DecorateRun(Run run)
    {
        var text = run.Text;

        // Collect both `[[wiki]]` links and bare http(s) URLs, then wrap each once.
        var decorations = new List<(int Index, int Length, Uri Uri, string Tooltip, string Expected)>();
        foreach (var (index, length, title) in TextExtraction.FindLinks(text))
        {
            decorations.Add((index, length,
                new Uri($"stickypad://note/{Uri.EscapeDataString(title)}", UriKind.Absolute),
                $"Open '{title}'", $"[[{title}]]"));
        }
        foreach (var (index, length, url) in TextExtraction.FindUrls(text))
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                decorations.Add((index, length, uri, url, url));
            }
        }
        if (decorations.Count == 0) return;

        // Replace from the back to keep earlier offsets stable.
        foreach (var (index, length, uri, tooltip, expected) in decorations.OrderByDescending(d => d.Index))
        {
            try
            {
                var startPtr = run.ContentStart.GetPositionAtOffset(index);
                var endPtr = run.ContentStart.GetPositionAtOffset(index + length);
                if (startPtr is null || endPtr is null) continue;

                var range = new TextRange(startPtr, endPtr);
                if (range.Text != expected) continue;

                var hyperlink = new Hyperlink(range.Start, range.End)
                {
                    NavigateUri = uri,
                    ToolTip = tooltip,
                };
                hyperlink.RequestNavigate += OnLinkRequestNavigate;
            }
            catch
            {
                // Pointer math can fail across complex inline structures — skip.
            }
        }
    }

    private void OnLinkRequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        e.Handled = true;

        if (e.Uri.Scheme is "http" or "https")
        {
            try
            {
                Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Couldn't open the link:\n{ex.Message}", "StickyPad",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            return;
        }

        // Otherwise it's a stickypad://note/{title} wiki link.
        var title = Uri.UnescapeDataString(e.Uri.AbsolutePath.TrimStart('/'));
        if (!_onLinkActivated(title))
        {
            MessageBox.Show(this, $"No note titled '{title}' was found.", "StickyPad",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private static IEnumerable<Run> CollectRuns(FlowDocument doc)
    {
        foreach (var block in doc.Blocks)
        {
            foreach (var run in CollectRuns(block))
            {
                yield return run;
            }
        }
    }

    private static IEnumerable<Run> CollectRuns(Block block)
    {
        switch (block)
        {
            case Paragraph p:
                foreach (var run in CollectRuns(p.Inlines)) yield return run;
                break;
            case Section s:
                foreach (var inner in s.Blocks)
                    foreach (var run in CollectRuns(inner)) yield return run;
                break;
            case List list:
                foreach (var item in list.ListItems)
                    foreach (var inner in item.Blocks)
                        foreach (var run in CollectRuns(inner)) yield return run;
                break;
            case BlockUIContainer:
                yield break;
        }
    }

    private static IEnumerable<Run> CollectRuns(InlineCollection inlines)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case Hyperlink:
                    // Already a link — skip to avoid nesting.
                    break;
                case Run r:
                    yield return r;
                    break;
                case Span span:
                    foreach (var inner in CollectRuns(span.Inlines)) yield return inner;
                    break;
            }
        }
    }

    private sealed class RelayCommandImpl : ICommand
    {
        private readonly Action<object?> _execute;
        public RelayCommandImpl(Action<object?> execute) => _execute = execute;
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => _execute(parameter);
        public event EventHandler? CanExecuteChanged { add { } remove { } }
    }
}
