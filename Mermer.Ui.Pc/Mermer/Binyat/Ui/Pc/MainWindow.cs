using AutoUpdaterDotNET;
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

    public Task CheckForUpdates(bool report = true)
    {
        try
        {
            // Сбрасываем старые подписки, чтобы не плодить дубли
            AutoUpdater.CheckForUpdateEvent -= AutoUpdaterOnCheckForUpdateEvent;

            AutoUpdater.ShowSkipButton = false;
            AutoUpdater.ShowRemindLaterButton = false;
            AutoUpdater.Mandatory = true;
            AutoUpdater.UpdateMode = Mode.ForcedDownload;

            // Считываем реальную версию запущенного exe-файла
            var currentVersion = Assembly.GetExecutingAssembly().GetName().Version;
            AutoUpdater.InstalledVersion = currentVersion;

            // Логируем текущую версию для отладки
            Console.WriteLine($"[UPDATER] Current Client Version: {currentVersion}");

            // Подписываемся на результат проверки
            AutoUpdater.CheckForUpdateEvent += AutoUpdaterOnCheckForUpdateEvent;

            string updateUrl = "http://localhost:5050/api/updates/version.xml";
            AutoUpdater.Start(updateUrl);
        }
        catch (Exception ex)
        {
            if (report)
            {
                ShowMessage("Update Error", $"Ошибка при инициализации обновления:\n{ex.Message}");
            }
        }

        return Task.CompletedTask;
    }

    private void AutoUpdaterOnCheckForUpdateEvent(UpdateInfoEventArgs args)
    {
        // Отписываемся сразу после разового срабатывания
        AutoUpdater.CheckForUpdateEvent -= AutoUpdaterOnCheckForUpdateEvent;

        Application.Current.Dispatcher.Invoke(() =>
        {
            if (args.Error != null)
            {
                Console.WriteLine($"[UPDATER ERROR] {args.Error.Message}");
                ShowMessage("Update Error", $"Не удалось проверить обновление:\n{args.Error.Message}");
                return;
            }

            Console.WriteLine($"[UPDATER] Server Version: {args.CurrentVersion}, IsAvailable: {args.IsUpdateAvailable}");

            if (args.IsUpdateAvailable)
            {
                // Явный запуск окна обновления AutoUpdater.NET
                try
                {
                    AutoUpdater.ShowUpdateForm(args);
                }
                catch (Exception ex)
                {
                    // Запасной запуск тихого скачивания, если форму заблокировал стек DevExpress
                    Console.WriteLine($"[UPDATER FORM ERROR] {ex.Message}");
                    AutoUpdater.DownloadUpdate(args);
                }
            }
            else
            {
                ShowMessage("No Updates", "У вас установлена самая актуальная версия программы.");
            }
        });
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