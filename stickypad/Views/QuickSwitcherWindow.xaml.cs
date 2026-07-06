using System.Windows;
using System.Windows.Input;
using StickyPad.Utils;
using StickyPad.ViewModels;

namespace StickyPad.Views;

/// 빠른 전환기(Ctrl+P) 팝업. 코드비하인드는 키 라우팅·포커스·위치 계산만 — 검색/열기 로직은 VM.
public partial class QuickSwitcherWindow : Window
{
    private readonly QuickSwitcherViewModel _viewModel;
    private bool _closing;

    public QuickSwitcherWindow(QuickSwitcherViewModel viewModel)
    {
        InitializeComponent();
        Icon = IconFactory.CreateAppIcon();
        _viewModel = viewModel;
        DataContext = viewModel;

        // 활성 모니터 중앙, 세로 30% 지점 — SystemParameters.WorkArea 기준 단순 배치.
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Left + (workArea.Width - Width) / 2;
        Top = workArea.Top + workArea.Height * 0.3;

        Loaded += async (_, _) =>
        {
            await _viewModel.LoadAsync().ConfigureAwait(true);
            QueryBox.Focus();
        };
        PreviewKeyDown += OnPreviewKeyDown;
        Deactivated += (_, _) => RequestClose();
        _viewModel.CloseRequested += RequestClose;
        Closed += (_, _) => _viewModel.CloseRequested -= RequestClose;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                _viewModel.MoveSelection(1);
                ScrollSelectedIntoView();
                e.Handled = true;
                break;
            case Key.Up:
                _viewModel.MoveSelection(-1);
                ScrollSelectedIntoView();
                e.Handled = true;
                break;
            case Key.Enter:
                _viewModel.OpenSelectedCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Escape:
                RequestClose();
                e.Handled = true;
                break;
        }
    }

    private void ScrollSelectedIntoView()
    {
        if (ResultsList.SelectedItem is not null) ResultsList.ScrollIntoView(ResultsList.SelectedItem);
    }

    private void ResultRow_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: QuickSwitcherItem item })
        {
            _viewModel.OpenNoteCommand.Execute(item);
        }
    }

    // Deactivated(포커스 이탈)와 VM 의 CloseRequested(선택 열기) 둘 다 이 창을 닫을 수 있어 중복 Close() 방지.
    private void RequestClose()
    {
        if (_closing) return;
        _closing = true;
        Close();
    }
}
