using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace StickyPad.Utils;

/// 오프라인 CodeMirror 6 기반 WYSIWYG 마크다운 에디터를 감싼 WebView2 브리지.
///
/// 보안: 렌더 전용 미리보기(<c>Preview</c>)는 스크립트를 끈 채 두고, 편집이 필요한 이
/// WebView 에만 스크립트/웹메시지를 허용한다. 대신 (1) 네트워크를 전면 차단(가상 호스트의
/// 로컬 자산만, CSP 는 페이지 <c>editor.html</c> 에서 <c>default-src 'none'</c>),
/// (2) 노트 내용은 HTML 주입이 아니라 <b>문자열 데이터</b>로만 주고받아(setMarkdown/getMarkdown)
/// 예전 XAML 가젯식 코드 실행 경로를 만들지 않는다.
public sealed class MarkdownWysiwyg
{
    private const string VirtualHost = "stickypad.editor";

    private readonly WebView2 _web;
    private TaskCompletionSource<bool>? _ready;
    private bool _initialized;

    /// 편집 내용(디바운스된 마크다운 소스)이 바뀌면 호출된다.
    public event Action<string>? Changed;

    public MarkdownWysiwyg(WebView2 web) => _web = web;

    public async Task InitAsync()
    {
        if (_initialized) return;

        var udf = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "StickyPad", "WebView2");
        Directory.CreateDirectory(udf);
        _web.CreationProperties = new CoreWebView2CreationProperties { UserDataFolder = udf };
        await _web.EnsureCoreWebView2Async().ConfigureAwait(true);

        var core = _web.CoreWebView2;
        var s = core.Settings;
        s.IsScriptEnabled = true;             // CM6 구동에 필요(이 편집 WebView 한정)
        s.IsWebMessageEnabled = true;         // 내용 동기화 채널
        s.AreHostObjectsAllowed = false;
        s.AreDevToolsEnabled = false;
        s.AreDefaultContextMenusEnabled = false;
        s.IsStatusBarEnabled = false;
        s.AreBrowserAcceleratorKeysEnabled = false;
        s.IsGeneralAutofillEnabled = false;
        s.IsPasswordAutosaveEnabled = false;

        // 임베드된 에디터 자산을 실제 폴더로 추출한 뒤 가상 호스트로 매핑 —
        // 로컬 자산만 로드, 교차 출처 차단. 단일 파일 배포에서도 동작한다.
        var assets = EnsureAssetsExtracted();
        core.SetVirtualHostNameToFolderMapping(
            VirtualHost, assets, CoreWebView2HostResourceAccessKind.DenyCors);

        core.WebMessageReceived += OnWebMessage;
        // 에디터 페이지 밖으로의 이동·새 창은 전부 차단.
        core.NavigationStarting += (_, e) =>
        {
            if (!e.Uri.StartsWith($"https://{VirtualHost}/", StringComparison.OrdinalIgnoreCase))
                e.Cancel = true;
        };
        core.NewWindowRequested += (_, e) => e.Handled = true;

        _initialized = true;
    }

    /// 에디터 페이지를 로드하고, 준비되면 마크다운을 주입한다.
    public async Task LoadAsync(string markdown)
    {
        await InitAsync().ConfigureAwait(true);
        _ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        // 앱 UI 언어를 에디터로 전달 — 툴바 툴팁·플레이스홀더 지역화.
        var lang = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        _web.CoreWebView2.Navigate($"https://{VirtualHost}/editor.html?lang={lang}");
        await _ready.Task.ConfigureAwait(true);       // 'ready' 메시지 대기
        await SetMarkdownAsync(markdown).ConfigureAwait(true);
    }

    public async Task SetMarkdownAsync(string markdown)
    {
        var json = JsonSerializer.Serialize(markdown ?? string.Empty);
        await _web.CoreWebView2.ExecuteScriptAsync($"window.SPEditor.setMarkdown({json})")
            .ConfigureAwait(true);
    }

    public async Task<string> GetMarkdownAsync()
    {
        var raw = await _web.CoreWebView2.ExecuteScriptAsync("window.SPEditor.getMarkdown()")
            .ConfigureAwait(true);
        // ExecuteScriptAsync 는 JSON 인코딩된 결과를 돌려준다.
        try { return JsonSerializer.Deserialize<string>(raw) ?? string.Empty; }
        catch { return string.Empty; }
    }

    public void Focus() =>
        _ = _web.CoreWebView2?.ExecuteScriptAsync("window.SPEditor && window.SPEditor.focus()");

    private static readonly object ExtractLock = new();
    private static string? _assetsDir;

    /// 어셈블리에 임베드된 에디터 자산을 로컬 폴더로 추출하고 그 경로를 돌려준다.
    /// 프로세스당 한 번만 추출한다(동시 창 생성 시 경합 방지).
    private static string EnsureAssetsExtracted()
    {
        lock (ExtractLock)
        {
            if (_assetsDir is not null) return _assetsDir;

            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "StickyPad", "mdeditor");
            Directory.CreateDirectory(dir);

            var asm = typeof(MarkdownWysiwyg).Assembly;
            ExtractResource(asm, "editor.html", Path.Combine(dir, "editor.html"));
            ExtractResource(asm, "mdeditor.bundle.js", Path.Combine(dir, "mdeditor.bundle.js"));

            _assetsDir = dir;
            return dir;
        }
    }

    private static void ExtractResource(Assembly asm, string endsWith, string destPath)
    {
        var name = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("." + endsWith, StringComparison.OrdinalIgnoreCase));
        if (name is null)
            throw new FileNotFoundException($"Embedded editor asset not found: {endsWith}");
        using var src = asm.GetManifestResourceStream(name)
            ?? throw new FileNotFoundException($"Cannot open embedded asset: {name}");
        using var fs = File.Create(destPath);
        src.CopyTo(fs);
    }

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string body;
        try { body = e.TryGetWebMessageAsString(); }
        catch { return; }
        if (string.IsNullOrEmpty(body)) return;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var type = doc.RootElement.GetProperty("type").GetString();
            if (type == "ready") { _ready?.TrySetResult(true); return; }
            if (type == "change" && doc.RootElement.TryGetProperty("md", out var md))
                Changed?.Invoke(md.GetString() ?? string.Empty);
        }
        catch { /* 형식 불명 메시지 무시 */ }
    }
}
