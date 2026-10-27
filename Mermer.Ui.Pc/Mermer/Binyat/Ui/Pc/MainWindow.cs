using DevExpress.Xpf.Core;
using DevExpress.Xpf.WindowsUI;
using Mermer.Common.Settings;
using Mermer.Services;
using Mermer.Ui.Pc.Helpers;
using Mermer.Ui.Pc.Services;
using MvvmCross.Core.ViewModels;
using MvvmCross.Localization;
using MvvmCross.Platform;
using Squirrel;
using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Orientation = System.Windows.Controls.Orientation;

namespace Mermer.Ui.Pc;

public partial class MainWindow : DXTabbedWindow
{
    public static MainWindow Instance { get; private set; }

    public MainWindow()
    {
        InitializeComponent();
        Instance = this;
        GridExtender.MainForm = this;
        ShortcutExtender.MainForm = this;

        // Устанавливаем заголовок динамически из AssemblyInfo
        var version = FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location).FileVersion;
        this.Title = $"Mermer ERP v{version}";
    }

    public ContentControl Root => ViewsRoot;

    private async void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!Mvx.Resolve<IConfigurator>().GetConfig<UpdateSettings>().CheckForUpdates)
            return;
        await CheckForUpdates(false);
    }

    public async Task CheckForUpdates(bool report = true)
    {
        try
        {
            var localizer = Mermer.Mvvm.Tools.LocalizationManager.Instance;

            bool? hasUpdates = await AppUpdaterService.CheckForUpdatesAsync();

            if (!hasUpdates.HasValue)
            {
                if (report)
                {
                    ShowMessage(
                        localizer.Get("Error Checking For Updates"),
                        localizer.Get("An error ocured while checking for updates, try again later!")
                    );
                }
            }
            else
            {
                if (hasUpdates.Value)
                {
                    string updatePrompt = localizer.Get(
                        "An updated version of this application is available.\nWould you like to update now?\n\nNew Version: {0}",
                        AppUpdaterService.UpdateVersion
                    );

                    bool? doUpdate = ShowMessage(
                        localizer.Get("Update Available"),
                        updatePrompt,
                        System.Windows.MessageBoxButton.YesNo
                    );

                    if (doUpdate.HasValue && doUpdate.Value)
                    {
                        ReleaseEntry releaseEntry = await AppUpdaterService.UpdateAsync();

                        string successMessage = localizer.Get(
                            "Application has been updated to version: {0}\nYou must restart application for changes to take effect.",
                            releaseEntry.Version
                        );

                        ShowMessage(localizer.Get("Application Updated"), successMessage);
                    }
                }
                else if (report)
                {
                    ShowMessage(
                        localizer.Get("No Updates Available"),
                        localizer.Get("No updates found, Application is up to date!")
                    );
                }
            }
        }
        catch (Exception ex)
        {
            ShowMessage("Update Error", $"Failed to check for updates.\nDetails: {ex.Message}");
        }
    }

    private bool? ShowMessage(string caption, string message, MessageBoxButton button = MessageBoxButton.OK)
    {
        StackPanel stackPanel = new StackPanel { Orientation = Orientation.Horizontal };
        stackPanel.Children.Add(new TextBlock { Text = message });

        WinUIDialogWindow winUiDialogWindow = new WinUIDialogWindow(caption, button)
        {
            Content = stackPanel
        };
        ThemeManager.SetThemeName(winUiDialogWindow, "Office2013DarkGray;Touch");
        return winUiDialogWindow.ShowDialog();
    }
}