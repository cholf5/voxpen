using Avalonia.Controls;
using Avalonia.Interactivity;
using VoxPen.App.ViewModels;

namespace VoxPen.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 用户点击关闭按钮时不退出应用，改为隐藏到托盘。
    /// 真正退出应通过托盘菜单或 ViewModel.ExitCommand。
    /// </summary>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm && !vm.IsExiting)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnClosing(e);
    }
}
