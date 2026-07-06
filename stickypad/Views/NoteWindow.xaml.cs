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
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Win32;
using StickyPad.Models;
using StickyPad.Resources;
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
    private readonly Func<string, Task> _onOpenFileRequested;
    private bool _suppressEditorSync;
    private bool _suppressToolbarSync;
    private bool _allowClose;
    private readonly DispatcherTimer _previewTimer;

    // 연동 파일의 외부 변경 감시 — 감시자 콜백은 스레드풀에서 오므로 UI 스레드로 넘겨 디바운스한다.
    private FileSystemWatcher? _fileWatcher;
    private readonly DispatcherTimer _fileReloadTimer;
    private bool _reloadPromptOpen;

    // WYSIWYG(오프라인 CodeMirror 6) 편집 — 마크다운 노트에서 토글로 켠다.
    private MarkdownWysiwyg? _wysiwyg;
    private bool _wysiwygOn;

    public NoteViewModel ViewModel => _viewModel;
    public IReadOnlyList<double> FontSizeChoices => NoteViewModel.FontSizeCatalog;

    /// WindowManager 가 .md 파일을 열 때 true 로 설정 — 로드되면 렌더링된 미리보기로 먼저 보여준다.
    public bool OpenInPreview { get; set; }

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
        Func<string, bool> onLinkActivated,
        Func<string, Task> onOpenFileRequested)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _onHidden = onHidden;
        _onNewRequested = onNewRequested;
        _onListRequested = onListRequested;
        _onLinkActivated = onLinkActivated;
        _onOpenFileRequested = onOpenFileRequested;

        Icon = IconFactory.CreateAppIcon();
        PopulateTemplateMenu();

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
        // 트레이로 숨긴 노트의 WebView2 메모리 사용을 낮춘다(다시 보이면 복원) — 베스트-에포트.
        IsVisibleChanged += (_, e) => SetWebViewMemoryLevel(low: !(bool)e.NewValue);
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(NoteViewModel.IsPreviewMode))
            {
                if (_wysiwygOn) return;   // WYSIWYG 편집 중엔 미리보기 토글이 레이아웃을 건드리지 않게
                UpdateToolbarVisibility();
                UpdateContentView();
            }
            else if (args.PropertyName == nameof(NoteViewModel.Color) && IsMarkup && _viewModel.IsPreviewMode)
            {
                RenderPreview();
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
        InputBindings.Add(new KeyBinding(
            new RelayCommandImpl(_ => OpenFileViaDialog()),
            new KeyGesture(Key.O, ModifierKeys.Control)));

        // In Markdown/HTML mode the editor is a plain source view — force paste to plain text
        // so pasted tags/markup are kept literally instead of being converted to rich text.
        DataObject.AddPastingHandler(Editor, OnEditorPasting);

        // Debounced live re-render for the split preview.
        _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _previewTimer.Tick += (_, _) =>
        {
            _previewTimer.Stop();
            if (IsMarkup && Preview.Visibility == Visibility.Visible) RenderPreview();
        };

        // 연동 파일이 외부에서 바뀌면(에디터가 여러 이벤트를 쏟아내므로) 잠시 모아 한 번만 반영.
        _fileReloadTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _fileReloadTimer.Tick += (_, _) => { _fileReloadTimer.Stop(); _ = ReloadLinkedFileAsync(); };

        Closed += (_, _) => DisposeFileWatcher();
    }

    private void OnEditorPasting(object sender, DataObjectPastingEventArgs e)
    {
        if (!IsMarkup) return; // rich mode keeps normal (formatted) paste

        if (e.DataObject.GetDataPresent(DataFormats.UnicodeText))
        {
            var text = e.DataObject.GetData(DataFormats.UnicodeText) as string ?? string.Empty;
            var plain = new DataObject();
            plain.SetData(DataFormats.UnicodeText, text);
            e.DataObject = plain;
            e.FormatToApply = DataFormats.UnicodeText;
        }
        else
        {
            // No text form (e.g. an image) — block it rather than embedding rich content.
            e.CancelCommand();
        }
    }

    public void RequestClose()
    {
        _allowClose = true;
        Close();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        LoadEditorContent();
        if (!IsMarkup) DecorateLinks();
        SyncModeButtons();
        // .md 파일을 열었을 때는 렌더링된 미리보기로 먼저 보여준다(편집은 Ctrl+E / 눈 아이콘).
        if (OpenInPreview && IsMarkup) _viewModel.IsPreviewMode = true;
        // Markup notes open in split view (editor + live preview) so they render immediately.
        UpdateToolbarVisibility();
        UpdateContentView();
        SyncToolbarFromSelection();
        StartFileWatcherIfLinked();
        // 앱이 꺼져 있는 동안 파일이 바뀌었을 수 있으니 로드 직후 한 번 대조(로컬 편집이 없으므로
        // 파일이 다르면 조용히 파일 내용으로 맞춘다 — 파일이 원본).
        if (_viewModel.IsLinkedFile) _ = ReloadLinkedFileAsync();
        if (!_viewModel.IsPreviewMode) Editor.Focus();

        // 마크다운 노트라면 저장된 선호에 따라 WYSIWYG 편집을 자동으로 켠다(연동/미리보기 열기 제외).
        if (_viewModel.Format == NoteContentFormat.Markdown && !OpenInPreview
            && !_viewModel.IsLinkedFile && PrefersWysiwyg())
        {
            WysiwygToggle.IsChecked = true;
            _ = EnterWysiwygAsync();
        }
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

            // 보안: TextRange.Load 는 비제한 XAML 파서라 가젯이 코드 실행을 일으킬 수 있다.
            // 정상 노트엔 없는 위험 마커가 보이면 XAML 로 로드하지 않고 순수 텍스트로 처리.
            if (_viewModel.Format == NoteContentFormat.RichTextXaml
                && !TextExtraction.ContainsDangerousXaml(_viewModel.Content))
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

            // Plain-text / Markdown / HTML source: one paragraph per line.
            Editor.Document.Blocks.Clear();
            foreach (var line in _viewModel.Content.Replace("\r\n", "\n").Split('\n'))
            {
                Editor.Document.Blocks.Add(new Paragraph(new Run(line)));
            }
        }
        finally
        {
            _suppressEditorSync = false;
        }
    }

    private void Editor_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressEditorSync) return;

        // Markdown/HTML notes store the raw source as plain text (rendered on preview).
        if (IsMarkup)
        {
            _viewModel.UpdateContent(GetEditorPlainText(), _viewModel.Format);
            if (Preview.Visibility == Visibility.Visible)
            {
                _previewTimer.Stop();
                _previewTimer.Start(); // debounce the live split re-render
            }
            return;
        }

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
        // The rich-text format bar only applies to rich notes, and never in preview.
        IsToolbarVisible = (IsActive || IsMouseOver) && !_viewModel.IsPreviewMode && !IsMarkup;
    }

    private bool IsMarkup =>
        _viewModel.Format is NoteContentFormat.Markdown or NoteContentFormat.Html;

    /// 숨겨진 노트의 WebView2 렌더러 메모리를 낮추고(다시 보이면 복원) — 트레이로 여러 노트를
    /// 감춰둘 때 백그라운드 메모리를 줄인다. 생성된 WebView 에만 적용, 실패해도 무해(hint).
    private void SetWebViewMemoryLevel(bool low)
    {
        var level = low
            ? CoreWebView2MemoryUsageTargetLevel.Low
            : CoreWebView2MemoryUsageTargetLevel.Normal;
        try { if (Preview.CoreWebView2 is { } p) p.MemoryUsageTargetLevel = level; } catch { /* best-effort */ }
        try { if (WysiwygEditor.CoreWebView2 is { } w) w.MemoryUsageTargetLevel = level; } catch { /* best-effort */ }
    }

    private string GetEditorPlainText() =>
        new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd).Text.TrimEnd('\r', '\n');

    private async void ModeToggle_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Primitives.ToggleButton { Tag: string tag }) return;
        var target = tag switch
        {
            "Markdown" => NoteContentFormat.Markdown,
            "Html" => NoteContentFormat.Html,
            _ => NoteContentFormat.RichTextXaml,
        };
        // 마크다운이 아닌 모드로 바꾸면 WYSIWYG 편집을 먼저 정리(내용 보존).
        if (_wysiwygOn && target != NoteContentFormat.Markdown)
        {
            WysiwygToggle.IsChecked = false;
            await ExitWysiwygAsync(save: true);
            SetWysiwygPreference(false);
        }
        SetMode(target);
    }

    // ── WYSIWYG(오프라인 CodeMirror 6) 마크다운 편집 ──────────────────────────
    private async void WysiwygToggle_OnClick(object sender, RoutedEventArgs e)
    {
        if (WysiwygToggle.IsChecked == true) await EnterWysiwygAsync();
        else await ExitWysiwygAsync(save: true);
        SetWysiwygPreference(WysiwygToggle.IsChecked == true);
    }

    private async Task EnterWysiwygAsync()
    {
        if (_viewModel.Format != NoteContentFormat.Markdown) { WysiwygToggle.IsChecked = false; return; }
        try
        {
            _wysiwyg ??= new MarkdownWysiwyg(WysiwygEditor);
            _wysiwyg.Changed -= OnWysiwygChanged;
            _wysiwyg.Changed += OnWysiwygChanged;

            await _wysiwyg.LoadAsync(GetEditorPlainText());   // 현재 소스 내용을 넘김
            _wysiwygOn = true;

            Editor.Visibility = Visibility.Collapsed;
            PreviewSplitter.Visibility = Visibility.Collapsed;
            Preview.Visibility = Visibility.Collapsed;
            WysiwygEditor.Visibility = Visibility.Visible;
            _viewModel.IsPreviewMode = false;
            _wysiwyg.Focus();
        }
        catch (Exception ex)
        {
            _wysiwygOn = false;
            WysiwygToggle.IsChecked = false;
            WysiwygEditor.Visibility = Visibility.Collapsed;
            UpdateContentView();
            MessageBox.Show(this, string.Format(Strings.Note_WysiwygUnavailable, ex.Message), "StickyPad",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task ExitWysiwygAsync(bool save)
    {
        if (!_wysiwygOn) return;
        try
        {
            if (save && _wysiwyg is not null)
            {
                var md = await _wysiwyg.GetMarkdownAsync();
                _suppressEditorSync = true;
                try { _viewModel.UpdateContent(md, NoteContentFormat.Markdown); LoadEditorContent(); }
                finally { _suppressEditorSync = false; }
            }
        }
        catch { /* 저장 실패해도 UI 는 복귀 */ }
        finally
        {
            _wysiwygOn = false;
            WysiwygEditor.Visibility = Visibility.Collapsed;
            UpdateContentView();
            if (!_viewModel.IsPreviewMode) Editor.Focus();
        }
    }

    private void OnWysiwygChanged(string md)
    {
        if (!_wysiwygOn) return;
        _viewModel.UpdateContent(md, NoteContentFormat.Markdown);   // 마크다운 소스로 저장
    }

    private static bool PrefersWysiwyg()
    {
        try { return App.Services.GetRequiredService<ISettingsService>().Current.PreferWysiwygMarkdown; }
        catch { return false; }
    }

    private static void SetWysiwygPreference(bool value)
    {
        try
        {
            var settings = App.Services.GetRequiredService<ISettingsService>();
            if (settings.Current.PreferWysiwygMarkdown != value)
            {
                settings.Current.PreferWysiwygMarkdown = value;
                _ = settings.SaveAsync();
            }
        }
        catch { /* 설정 저장 실패는 치명적이지 않음 */ }
    }

    private void SetMode(NoteContentFormat target)
    {
        var targetIsMarkup = target is NoteContentFormat.Markdown or NoteContentFormat.Html;
        // Treat PlainText and RichTextXaml as the same "rich" bucket.
        if (target == _viewModel.Format || (!IsMarkup && !targetIsMarkup))
        {
            SyncModeButtons();
            return;
        }

        // Carry the current text across the switch (rich formatting is dropped when leaving rich mode).
        var text = GetEditorPlainText();

        _suppressEditorSync = true;
        try
        {
            if (targetIsMarkup)
            {
                _viewModel.UpdateContent(text, target);   // Content = raw source
                LoadEditorContent();
            }
            else
            {
                _viewModel.Format = NoteContentFormat.RichTextXaml;
                Editor.Document.Blocks.Clear();
                foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
                {
                    Editor.Document.Blocks.Add(new Paragraph(new Run(line)));
                }
            }
        }
        finally
        {
            _suppressEditorSync = false;
        }

        if (!targetIsMarkup)
        {
            _viewModel.IsPreviewMode = false;
            Editor_OnTextChanged(Editor, null!);   // persist the new XAML
        }

        SyncModeButtons();
        UpdateToolbarVisibility();
        UpdateContentView();
        if (!_viewModel.IsPreviewMode) Editor.Focus();
    }

    private void SyncModeButtons()
    {
        ModeTextToggle.IsChecked = !IsMarkup;
        ModeMarkdownToggle.IsChecked = _viewModel.Format == NoteContentFormat.Markdown;
        ModeHtmlToggle.IsChecked = _viewModel.Format == NoteContentFormat.Html;
        // WYSIWYG 편집은 마크다운 노트에서만 의미가 있다.
        WysiwygToggle.IsEnabled = _viewModel.Format == NoteContentFormat.Markdown;
    }

    private void UpdateContentView()
    {
        if (!IsMarkup)
        {
            // Rich note: editor fills the area, no preview.
            Editor.Visibility = Visibility.Visible;
            PreviewSplitter.Visibility = Visibility.Collapsed;
            Preview.Visibility = Visibility.Collapsed;
            EditorRow.Height = new GridLength(1, GridUnitType.Star);
            SplitterRow.Height = new GridLength(0);
            PreviewRow.Height = new GridLength(0);
            return;
        }

        if (_viewModel.IsPreviewMode)
        {
            // Full rendered preview (Ctrl+E / Eye toggled on).
            Editor.Visibility = Visibility.Collapsed;
            PreviewSplitter.Visibility = Visibility.Collapsed;
            Preview.Visibility = Visibility.Visible;
            EditorRow.Height = new GridLength(0);
            SplitterRow.Height = new GridLength(0);
            PreviewRow.Height = new GridLength(1, GridUnitType.Star);
        }
        else
        {
            // Split: editor on top, live-rendered preview below.
            Editor.Visibility = Visibility.Visible;
            PreviewSplitter.Visibility = Visibility.Visible;
            Preview.Visibility = Visibility.Visible;
            if (EditorRow.Height.Value <= 0 || PreviewRow.Height.Value <= 0)
            {
                EditorRow.Height = new GridLength(1, GridUnitType.Star);
                PreviewRow.Height = new GridLength(1, GridUnitType.Star);
            }
            SplitterRow.Height = GridLength.Auto;
        }
        RenderPreview();
    }

    private async void RenderPreview()
    {
        try
        {
            var html = HtmlRenderer.Render(GetEditorPlainText(), _viewModel.Format, _viewModel.Theme);
            await EnsureWebViewAsync().ConfigureAwait(true);
            Preview.NavigateToString(html);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                string.Format(Strings.Note_PreviewError, ex.Message),
                "StickyPad", MessageBoxButton.OK, MessageBoxImage.Warning);
            _viewModel.IsPreviewMode = false;
        }
    }

    private bool _webViewReady;

    private async Task EnsureWebViewAsync()
    {
        if (_webViewReady) return;

        var udf = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "StickyPad", "WebView2");
        Directory.CreateDirectory(udf);
        Preview.CreationProperties = new CoreWebView2CreationProperties { UserDataFolder = udf };
        await Preview.EnsureCoreWebView2Async().ConfigureAwait(true);

        var core = Preview.CoreWebView2;
        // Notes never need scripting — block it so pasted/embedded HTML can't run code.
        core.Settings.IsScriptEnabled = false;
        core.Settings.AreHostObjectsAllowed = false;
        core.Settings.IsWebMessageEnabled = false;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;

        // Real links open in the user's browser instead of navigating inside the note.
        core.NavigationStarting += (_, e) =>
        {
            if (e.IsUserInitiated && (e.Uri.StartsWith("http://") || e.Uri.StartsWith("https://")))
            {
                e.Cancel = true;
                OpenExternal(e.Uri);
            }
        };
        core.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            // 스킴을 http/https 로 제한 — 노트 마크업이 file:/UNC/프로토콜 핸들러로 로컬 실행을
            // 유도하는 것을 막는다(NavigationStarting 과 동일 정책).
            if (e.Uri.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || e.Uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                OpenExternal(e.Uri);
            }
        };
        _webViewReady = true;
    }

    private static void OpenExternal(string uri)
    {
        try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); }
        catch { /* ignore */ }
    }

    // ── 파일 열기(대화상자 / 드래그&드롭) ─────────────────────────────────────

    private async void OpenFileViaDialog()
    {
        var dlg = new OpenFileDialog
        {
            Filter = LinkedFile.OpenDialogFilter,
            Title = Strings.Tray_OpenMarkdownDialogTitle,
        };
        if (dlg.ShowDialog(this) == true)
        {
            try { await _onOpenFileRequested(dlg.FileName); }
            catch (Exception ex)
            {
                MessageBox.Show(this, string.Format(Strings.Note_OpenFileError, ex.Message), "StickyPad",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    private void OpenFileButton_OnClick(object sender, RoutedEventArgs e) => OpenFileViaDialog();

    private void Window_OnPreviewDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private async void Window_OnPreviewDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        e.Handled = true;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;

        foreach (var file in files)
        {
            if (!LinkedFile.IsSupported(file)) continue;
            try { await _onOpenFileRequested(file); }
            catch (Exception ex)
            {
                MessageBox.Show(this, string.Format(Strings.Note_OpenFileError, ex.Message), "StickyPad",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    // ── 연동 파일 외부 변경 감시 ──────────────────────────────────────────────

    private void StartFileWatcherIfLinked()
    {
        if (!_viewModel.IsLinkedFile) return;
        var path = _viewModel.LinkedFilePath!;
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(dir)) return;

            _fileWatcher = new FileSystemWatcher(dir, Path.GetFileName(path))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            };
            _fileWatcher.Changed += OnLinkedFileEvent;
            _fileWatcher.Created += OnLinkedFileEvent;
            _fileWatcher.Renamed += OnLinkedFileEvent;
            _fileWatcher.EnableRaisingEvents = true;
        }
        catch (Exception)
        {
            // 감시는 부가 기능 — 실패해도 편집/저장은 정상 동작.
            DisposeFileWatcher();
        }
    }

    private void OnLinkedFileEvent(object sender, FileSystemEventArgs e)
    {
        // 콜백은 스레드풀 스레드 — UI 로 넘겨 디바운스 타이머를 재시작.
        Dispatcher.BeginInvoke(() => { _fileReloadTimer.Stop(); _fileReloadTimer.Start(); });
    }

    private async Task ReloadLinkedFileAsync()
    {
        if (!_viewModel.IsLinkedFile || _reloadPromptOpen) return;
        var path = _viewModel.LinkedFilePath!;

        string fileText;
        try { (fileText, _) = await LinkedFile.ReadAsync(path).ConfigureAwait(true); }
        catch { return; }   // 파일이 잠시 잠겼거나 삭제 중 — 다음 이벤트에서 재시도

        var editorText = GetEditorPlainText();
        if (string.Equals(fileText, editorText, StringComparison.Ordinal))
        {
            // 우리가 방금 쓴 내용이거나 실질 변화 없음 — 동기화 기준만 갱신.
            _viewModel.MarkSynced(fileText);
            return;
        }

        var hasLocalEdits = !string.Equals(editorText, _viewModel.LastSyncedText, StringComparison.Ordinal);
        if (hasLocalEdits)
        {
            _reloadPromptOpen = true;
            try
            {
                var choice = MessageBox.Show(this,
                    Strings.Note_FileChangedPrompt,
                    Strings.Note_FileChangedTitle,
                    MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
                if (choice != MessageBoxResult.Yes) return;
            }
            finally { _reloadPromptOpen = false; }
        }

        ReplaceEditorText(fileText);
        _viewModel.UpdateContent(fileText, _viewModel.Format);
        _viewModel.MarkSynced(fileText);
        if (IsMarkup && Preview.Visibility == Visibility.Visible) RenderPreview();
    }

    private void ReplaceEditorText(string text)
    {
        _suppressEditorSync = true;
        try
        {
            Editor.Document.Blocks.Clear();
            foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
            {
                Editor.Document.Blocks.Add(new Paragraph(new Run(line)));
            }
        }
        finally
        {
            _suppressEditorSync = false;
        }
    }

    private void DisposeFileWatcher()
    {
        _fileReloadTimer?.Stop();
        if (_fileWatcher is null) return;
        try
        {
            _fileWatcher.EnableRaisingEvents = false;
            _fileWatcher.Changed -= OnLinkedFileEvent;
            _fileWatcher.Created -= OnLinkedFileEvent;
            _fileWatcher.Renamed -= OnLinkedFileEvent;
            _fileWatcher.Dispose();
        }
        catch { /* nothing to do */ }
        _fileWatcher = null;
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
        var message = _viewModel.IsLinkedFile
            ? string.Format(Strings.Note_DeleteLinkedConfirm, Path.GetFileName(_viewModel.LinkedFilePath!))
            : Strings.Note_DeleteConfirm;
        var confirm = MessageBox.Show(
            this,
            message,
            "StickyPad",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning,
            MessageBoxResult.Cancel);
        if (confirm != MessageBoxResult.OK) return;
        await _viewModel.DeleteCommand.ExecuteAsync(null).ConfigureAwait(true);
    }

    private void PopulateTemplateMenu()
    {
        foreach (var template in NoteTemplates.All)
        {
            var item = new MenuItem { Header = template.Name() };
            item.Click += (_, _) => App.Services.GetRequiredService<IWindowManager>().CreateAndShowNew(template);
            TemplateMenu.Items.Add(item);
        }
    }

    private void MoreMenu_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { ContextMenu: { } menu } b)
        {
            menu.PlacementTarget = b;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }
    }

    private async void ExportNotePdf_OnClick(object sender, RoutedEventArgs e) => await ExportCurrentNoteAsync(ExportFormat.Pdf);
    private async void ExportNoteHtml_OnClick(object sender, RoutedEventArgs e) => await ExportCurrentNoteAsync(ExportFormat.Html);
    private async void ExportNoteMarkdown_OnClick(object sender, RoutedEventArgs e) => await ExportCurrentNoteAsync(ExportFormat.MarkdownFiles);

    private async Task ExportCurrentNoteAsync(ExportFormat format)
    {
        try
        {
            await _viewModel.FlushAsync().ConfigureAwait(true); // 최신 내용 반영
            await App.Services.GetRequiredService<IBackupService>().ExportSingleNoteAsync(_viewModel.Note, format).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, string.Format(Strings.Note_ExportError, ex.Message), "StickyPad", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void PrintNote_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await _viewModel.FlushAsync().ConfigureAwait(true);
            await App.Services.GetRequiredService<IBackupService>().PrintNoteAsync(_viewModel.Note).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, string.Format(Strings.Note_PrintError, ex.Message), "StickyPad", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
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
                MessageBox.Show(this, string.Format(Strings.Note_OpenLinkError, ex.Message), "StickyPad",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            return;
        }

        // Otherwise it's a stickypad://note/{title} wiki link.
        var title = Uri.UnescapeDataString(e.Uri.AbsolutePath.TrimStart('/'));
        if (!_onLinkActivated(title))
        {
            MessageBox.Show(this, string.Format(Strings.Note_NoteNotFound, title), "StickyPad",
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
