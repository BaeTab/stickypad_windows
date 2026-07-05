using System.Linq;
using System.Windows;
using Microsoft.Win32;

namespace StickyPad.Utils;

/// 트레이 컨텍스트 메뉴에서 소유자 없이 파일/폴더 대화상자를 띄우면(특히 .NET 8 의
/// <see cref="OpenFolderDialog"/>) 닫히는 메뉴 팝업에 붙어 "떴다가 즉시 사라지는" 문제가 있다.
/// 항상 살아 있는 소유자 창을 붙여 안정적으로 표시한다 — 보이는 창이 없으면(모든 노트가
/// 숨겨진 순수 트레이 상태) 화면 중앙에 잠깐 투명한 소유자 창을 만들어 owner 로 쓴다.
public static class DialogOwner
{
    public static bool? Show(CommonDialog dialog)
    {
        var owner = Application.Current?.Windows.OfType<Window>()
            .FirstOrDefault(w => w.IsVisible && w.IsLoaded);
        if (owner is not null) return dialog.ShowDialog(owner);

        // 보이는 창이 하나도 없을 때: 화면 중앙에 1x1 투명 창을 소유자로 세운다(사용자 눈엔
        // 보이지 않지만, 대화상자가 붙을 실제 HWND 가 존재하므로 사라지지 않고 중앙에 뜬다).
        var wa = SystemParameters.WorkArea;
        var transient = new Window
        {
            Width = 1,
            Height = 1,
            Left = wa.Left + wa.Width / 2,
            Top = wa.Top + wa.Height / 2,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = null,
            Opacity = 0,
            ShowActivated = false,
        };
        transient.Show();
        try { return dialog.ShowDialog(transient); }
        finally { transient.Close(); }
    }
}
