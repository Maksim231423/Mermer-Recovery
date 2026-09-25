using DevExpress.Xpf.Core;
using DevExpress.Xpf.Grid;
using DevExpress.Xpf.WindowsUI;
using Mermer.Mvvm.Messages;
using Mermer.Mvvm.ViewModels;
using Mermer.Ui.Core.ViewModels;
using MvvmCross.Core.ViewModels;
using MvvmCross.Wpf.Views.Presenters;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Mermer.Ui.Pc
{
    public class MainViewPresenter : MvxBaseWpfViewPresenter
    {
        private readonly ContentControl _contentControl;
        private static DXTabControl _tabControl;
        private static readonly ObservableCollection<FrameworkElement> TabItems = new ObservableCollection<FrameworkElement>();
        private static readonly List<WinUIDialogWindow> Dialogs = new List<WinUIDialogWindow>();

        public MainViewPresenter(ContentControl contentControl) => this._contentControl = contentControl;

        public static void SetTabControl(DXTabControl tabControl)
        {
            MainViewPresenter._tabControl = tabControl;
            MainViewPresenter._tabControl.ItemsSource = MainViewPresenter.TabItems;
            MainViewPresenter._tabControl.TabHiding += TabControl_TabHiding;
        }

        private static void TabControl_TabHiding(object sender, TabControlTabHidingEventArgs e)
        {
            e.Cancel = true;

            if (e.Item is FrameworkElement viewToKill)
            {
                CloseTab(viewToKill, "TabHeaderCross");
            }
        }

        private static void CloseTab(FrameworkElement viewToKill, string source)
        {
            if (viewToKill == null) return;

            // 1. Моментально скрываем вкладку визуально
            viewToKill.Visibility = Visibility.Collapsed;

            // 2. Сразу удаляем вкладку из TabItems, НЕ ТРОГАЯ внутренности грида
            MainViewPresenter.TabItems.Remove(viewToKill);

            // 3. Отложенное уничтожение контекста через Dispatcher с минимальным приоритетом
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    if (viewToKill.DataContext is IDisposable disposable)
                    {
                        disposable.Dispose();
                    }
                    viewToKill.DataContext = null;
                }
                catch { }
            }), DispatcherPriority.ApplicationIdle);
        }

        public override void Present(FrameworkElement frameworkElement)
        {
            object dataContext = frameworkElement.DataContext;
            if (dataContext is IDialogViewModel dialogViewModel)
            {
                frameworkElement.SetValue(ThemeManager.ThemeNameProperty, "HybridApp");
                WinUIDialogWindow dialog = new WinUIDialogWindow(dialogViewModel.Caption ?? dataContext.GetType().Name);
                dialog.Content = frameworkElement;
                dialog.SetValue(ThemeManager.ThemeNameProperty, "Office2013DarkGray");
                MainViewPresenter.Dialogs.Add(dialog);
                Task.Run(() => Application.Current.Dispatcher.Invoke(() => dialog.ShowDialog()));
            }
            else
            {
                if (dataContext != null && dataContext.GetType().Name.Contains("LoginViewModel"))
                {
                    MainViewPresenter._tabControl = null;
                    MainViewPresenter.TabItems.Clear();
                    this._contentControl.Content = frameworkElement;
                    return;
                }

                if (MainViewPresenter._tabControl != null)
                {
                    if (dataContext != null && dataContext.GetType().Name.Contains("MainViewModel")) return;

                    Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        MainViewPresenter.TabItems.Insert(MainViewPresenter._tabControl.SelectedIndex + 1, frameworkElement);
                        MainViewPresenter._tabControl.SelectedItem = frameworkElement;
                    }), System.Windows.Threading.DispatcherPriority.Loaded);

                    return;
                }

                this._contentControl.Content = frameworkElement;
            }
        }

        public override void ChangePresentation(MvxPresentationHint hint)
        {
            if (hint is MvxClosePresentationHint closeHint)
            {
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        if (MainViewPresenter.Dialogs.Count > 0)
                        {
                            var dialog = MainViewPresenter.Dialogs.Last();
                            dialog.Close();
                            MainViewPresenter.Dialogs.Remove(dialog);
                            return;
                        }

                        var viewToKill = MainViewPresenter.TabItems.FirstOrDefault(v => v.DataContext == closeHint.ViewModelToClose);
                        if (viewToKill != null)
                        {
                            CloseTab(viewToKill, "CloseCommand");
                        }
                        else
                        {
                            if (_tabControl?.SelectedItem is FrameworkElement currentTab)
                            {
                                CloseTab(currentTab, "CurrentSelectedTabFallback");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[CLOSE ERROR]: {ex.Message}");
                    }
                }), DispatcherPriority.Normal);
                return;
            }

            if (hint is MvxCloseAppPresentationHint)
            {
                Application.Current.MainWindow?.Close();
                return;
            }

            base.ChangePresentation(hint);
        }

        public override void Close(IMvxViewModel toClose)
        {
            this.ChangePresentation(new MvxClosePresentationHint(toClose));
        }

        public bool CloseAll(MvxCloseAllPresentationHint hint)
        {
            return true;
        }

        private static T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) return null;
            int childrenCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childrenCount; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typed) return typed;
                var found = FindVisualChild<T>(child);
                if (found != null) return found;
            }
            return null;
        }
    }
}