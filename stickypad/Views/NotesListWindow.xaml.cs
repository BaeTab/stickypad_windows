using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using StickyPad.Utils;
using StickyPad.ViewModels;

namespace StickyPad.Views;

public partial class NotesListWindow : Window
{
    private readonly NotesListViewModel _viewModel;

    public NotesListWindow(NotesListViewModel viewModel)
    {
        InitializeComponent();
        Icon = IconFactory.CreateAppIcon();
        _viewModel = viewModel;
        DataContext = viewModel;
        Loaded += async (_, _) => { await _viewModel.ReloadAsync(); SearchBox.Focus(); };
        Closing += OnClosing;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        // Hide instead of close so we don't have to rebuild the VM each time the user toggles the list.
        e.Cancel = true;
        Hide();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Hide();
            e.Handled = true;
        }
    }

    private void Card_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is NoteSummary summary)
        {
            // 휴지통 카드를 클릭으로 여는 건 의도가 모호하니 무시 — 복원/영구삭제 버튼만 받는다.
            if (summary.IsTrashed) return;
            _viewModel.OpenCommand.Execute(summary);
        }
    }

    private void ActiveTab_OnClick(object sender, RoutedEventArgs e)
    {
        // 활성 탭은 단방향 토글(켜진 상태에서 다시 누르면 그대로 유지). XAML 의 IsChecked 가
        // OneWay 바인딩이라 직접 ShowTrash 를 끈다.
        _viewModel.ShowTrash = false;
    }

    public async System.Threading.Tasks.Task ShowAndReloadAsync()
    {
        await _viewModel.ReloadAsync().ConfigureAwait(true);
        if (!IsVisible) Show();
        Activate();
        SearchBox.Focus();
        SearchBox.SelectAll();
    }
}
