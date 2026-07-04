using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace StickyPad.Utils;

/// HTML 문서를 오프스크린 WebView2 로 렌더해 PDF 파일로 저장한다.
/// WPF 의 WebView2 컨트롤은 HWND 가 있어야 초기화되므로, 화면 밖 숨김 창에 얹어
/// 렌더한 뒤 곧바로 닫는다. 반드시 UI(STA) 스레드에서 호출해야 한다.
public static class PdfRenderer
{
    private static readonly TimeSpan NavigationTimeout = TimeSpan.FromSeconds(30);

    public static async Task RenderAsync(string html, string pdfPath)
    {
        // NavigateToString 은 약 2MB 제한이 있어(이미지 임베드 대비) 임시 파일로 우회한다.
        var tempHtml = Path.Combine(Path.GetTempPath(), $"stickypad-export-{Guid.NewGuid():N}.html");
        await File.WriteAllTextAsync(tempHtml, html).ConfigureAwait(true);

        // 노트 미리보기(WebView2)와 분리된 전용 폴더 — 일회성 인쇄 환경이 상시 미리보기 환경과
        // 락/캐시를 공유하지 않게 한다.
        var udf = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "StickyPad", "WebView2-export");
        Directory.CreateDirectory(udf);

        // CreationProperties 는 트리에 얹기 전에 설정하는 게 견고하다.
        var web = new WebView2 { CreationProperties = new CoreWebView2CreationProperties { UserDataFolder = udf } };
        var host = new Window
        {
            Width = 900,
            Height = 1200,
            // 화면 밖으로 숨긴다. Opacity=0 은 창을 레이어드(투명) 창으로 만들어 WebView2 호스팅이
            // 거부(ERROR_INVALID_STATE)되므로 쓰지 않는다 — 위치만으로 사용자 시야에서 가린다.
            Left = -32000,
            Top = -32000,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            ShowActivated = false,
            IsHitTestVisible = false,
            Content = web,
        };

        try
        {
            host.Show();   // HWND 확보 — WebView2 초기화의 전제 조건.
            await web.EnsureCoreWebView2Async().ConfigureAwait(true);

            // 노트 렌더와 동일하게 스크립트/호스트 통신 전면 차단.
            var settings = web.CoreWebView2.Settings;
            settings.IsScriptEnabled = false;
            settings.AreHostObjectsAllowed = false;
            settings.IsWebMessageEnabled = false;
            settings.AreDevToolsEnabled = false;
            settings.AreDefaultContextMenusEnabled = false;

            var navigated = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnNav(object? _, CoreWebView2NavigationCompletedEventArgs e) => navigated.TrySetResult(e.IsSuccess);
            // 렌더러 프로세스가 죽으면 NavigationCompleted 가 영영 안 올 수 있으니 예외로 깨운다.
            void OnProcFailed(object? _, CoreWebView2ProcessFailedEventArgs e) =>
                navigated.TrySetException(new InvalidOperationException(
                    "WebView2 프로세스가 예기치 않게 종료되었습니다: " + e.ProcessFailedKind));

            web.NavigationCompleted += OnNav;
            web.CoreWebView2.ProcessFailed += OnProcFailed;
            try
            {
                web.CoreWebView2.Navigate(new Uri(tempHtml).AbsoluteUri);
                // 로컬 파일 렌더가 시간 내에 끝나지 않으면(멈춤 방지) 타임아웃 처리.
                if (await Task.WhenAny(navigated.Task, Task.Delay(NavigationTimeout)).ConfigureAwait(true) != navigated.Task)
                {
                    throw new TimeoutException("문서 렌더링이 제한 시간 안에 끝나지 않았습니다.");
                }
                var success = await navigated.Task.ConfigureAwait(true);   // 예외(ProcessFailed) 전파.
                if (!success) throw new InvalidOperationException("내보낼 문서를 렌더링하지 못했습니다.");
            }
            finally
            {
                web.NavigationCompleted -= OnNav;
                web.CoreWebView2.ProcessFailed -= OnProcFailed;
            }

            var print = web.CoreWebView2.Environment.CreatePrintSettings();
            print.ShouldPrintBackgrounds = true;
            var ok = await web.CoreWebView2.PrintToPdfAsync(pdfPath, print).ConfigureAwait(true);
            if (!ok) throw new InvalidOperationException("WebView2 PrintToPdf 가 실패를 반환했습니다.");
        }
        finally
        {
            web.Dispose();
            host.Close();
            try { File.Delete(tempHtml); } catch { /* 임시 파일 정리 실패는 무시 */ }
        }
    }
}
