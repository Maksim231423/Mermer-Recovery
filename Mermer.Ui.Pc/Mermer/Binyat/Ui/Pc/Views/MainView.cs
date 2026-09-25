using DevExpress.Xpf.Core;
using DevExpress.Xpf.WindowsUI;
using Mermer.Mvvm.ViewModels;
using Mermer.Ui.Core.ViewModels;
using Mermer.Ui.Pc.Reports;
using Mermer.Ui.Pc.ViewModels;
using MvvmCross.Core.Navigation;
using MvvmCross.Core.ViewModels;
using MvvmCross.Platform;
using MvvmCross.Wpf.Views;
using MvvmCross.Wpf.Views.Presenters;
using System;
using MvvmCross.Core.Views;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Mermer.Ui.Pc.Views;

public partial class MainView : MvxWpfView
{
    private HamburgerMenu _menu;

    private System.Diagnostics.Stopwatch _renderSw;

    public MainView()
    {
        _renderSw = System.Diagnostics.Stopwatch.StartNew();
        InitializeComponent();
        Console.WriteLine($"[PERF UI] MainView InitializeComponent: {_renderSw.ElapsedMilliseconds} ms");

        this.Loaded += (s, e) =>
        {
            Console.WriteLine($"[PERF UI] MainView Fully Loaded & Rendered: {_renderSw.ElapsedMilliseconds} ms");
        };
    }

    private void VersionTextLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is TextBlock textBlock)
        {
            FileVersionInfo versionInfo = FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location);
            textBlock.Text = "|  " + versionInfo.FileVersion;
        }
    }

    private async void MvxWpfView_Loaded(object sender, RoutedEventArgs e)
    {
        MainViewPresenter.SetTabControl(TabControl);
        if (DataContext is MainViewModel dataContext && dataContext.OpenPosOnLoad)
        {
            dataContext.ShowPosCommand.Execute(null);
        }
        // await CreateDumbReport().ConfigureAwait(false);
    }

    private Task CreateDumbReport()
    {
        return Task.Run(() =>
        {
            ReportStandard reportStandard = new ReportStandard();
            reportStandard.CreateDocument();
            reportStandard.PrintingSystem.Document.AutoFitToPagesWidth = 1;
        });
    }

    private void TabHiding(object sender, DevExpress.Xpf.Core.TabControlTabHidingEventArgs e)
    {
        if (e.Item is FrameworkElement frameworkElement && frameworkElement.DataContext is BaseViewModel dataContext)
        {
            e.Cancel = true;
            dataContext.CloseCommand.Execute(null);
        }
    }

    private void Frame_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;

        if (MainWindow.Instance.WindowState == WindowState.Maximized)
            MainWindow.Instance.WindowState = WindowState.Normal;

        MainWindow.Instance.DragMove();
    }

    private void Frame_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (MainWindow.Instance.WindowState == WindowState.Maximized)
        {
            MainWindow.Instance.WindowState = WindowState.Normal;
        }
        else if (MainWindow.Instance.WindowState == WindowState.Normal)
        {
            MainWindow.Instance.WindowState = WindowState.Maximized;
        }
    }

    private async void UpdateApplication(object sender, RoutedEventArgs e)
    {
        await MainWindow.Instance.CheckForUpdates();
    }

    private void OnMenuLoaded(object sender, RoutedEventArgs e)
    {
        _menu = (HamburgerMenu)sender;
    }

    private async void CheckUpdatesBtn_Click(object sender, System.Windows.RoutedEventArgs e)
    {

        if (Mermer.Ui.Pc.MainWindow.Instance != null)
        {
            await Mermer.Ui.Pc.MainWindow.Instance.CheckForUpdates(true);
        }
    }

    private void OnSubMenuClick(object sender, RoutedEventArgs e)
    {
        if (!_menu.IsInitiallyCompact || _menu.IsCompact) return;

        _menu.ViewState = _menu.ViewState == HamburgerMenuViewState.Overlay
            ? HamburgerMenuViewState.CompactOverlay
            : HamburgerMenuViewState.Closed;
    }

    private void ShowPrintLayoutConfig(object sender, RoutedEventArgs e)
    {
        Mvx.IocConstruct<IMvxNavigationService>().Navigate<ReportsListViewModel>();
    }

    private void TabCloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.TemplatedParent is DevExpress.Xpf.Core.DXTabItem tabItem)
        {
            var view = tabItem.Content as FrameworkElement ?? tabItem.DataContext as FrameworkElement;


            if (view?.DataContext is BaseViewModel viewModel)
            {
                if (viewModel.CloseCommand != null && viewModel.CloseCommand.CanExecute(null))
                {
                    
                    viewModel.CloseCommand.Execute(null);
                }
            }
        }
    }
}