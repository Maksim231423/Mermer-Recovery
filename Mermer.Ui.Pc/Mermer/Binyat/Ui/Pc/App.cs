using DevExpress.Xpf.Core;
using DevExpress.Xpf.Editors;
using DevExpress.Xpf.Editors.Settings;
using DevExpress.Xpf.Grid;
using Mermer.Mvvm.Messages;
using Mermer.Ui.Pc.Helpers;
using MvvmCross.Core.ViewModels;
using MvvmCross.Platform;
using MvvmCross.Wpf.Views.Presenters;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;

namespace Mermer.Ui.Pc;

public partial class App : System.Windows.Application
{
    private bool _setupComplete;

    public App()
    {
        // 1. Берем культуру динамически из настроек текущей системы Windows:
        var culture = CultureInfo.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = CultureInfo.CurrentUICulture;

        FrameworkElement.LanguageProperty.OverrideMetadata(
            typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(culture.IetfLanguageTag)));

        // 2. Перехват колонок при их автоматической генерации гридом
        EventManager.RegisterClassHandler(
            typeof(GridControl),
            GridControl.AutoGeneratingColumnEvent,
            new AutoGeneratingColumnEventHandler(OnGridAutoGeneratingColumn)
        );

        // 3. Перехват колонок при загрузке любого грида
        EventManager.RegisterClassHandler(
            typeof(GridControl),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnGridLoaded)
        );
    }

    [Obsolete]
    private void DoSetup()
    {
        if (_setupComplete) return;

        LoadMvxAssemblyResources();

        if (this.MainWindow == null)
        {
            this.MainWindow = new MainWindow();
        }

        MainViewPresenter presenter = new MainViewPresenter(((MainWindow)this.MainWindow).Root);

        presenter.AddPresentationHintHandler<MvxCloseAllPresentationHint>(hint =>
        {
            if (hint != null)
            {
                return presenter.CloseAll(hint);
            }
            return false;
        });

        new Setup(this.Dispatcher, presenter).Initialize();

        Mvx.RegisterType<IMvxCommandHelper, MvxWpfCommandHelper>();
        Mvx.Resolve<IMvxAppStart>().Start();

        _setupComplete = true;

        DevExpress.Xpf.Core.ApplicationThemeHelper.ApplicationThemeName = "HybridApp";
        DXGridDataController.DisableThreadingProblemsDetection = true;

        this.MainWindow.Show();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DoSetup();
    }

    private void LoadMvxAssemblyResources()
    {
        int num = 0;
        while (this.TryFindResource("MvxAssemblyImport" + num) != null)
            ++num;
    }

    private static void OnGridAutoGeneratingColumn(object sender, AutoGeneratingColumnEventArgs e)
    {
        FormatColumn(e.Column);
    }

    private static void OnGridLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is GridControl grid)
        {
            foreach (var col in grid.Columns)
            {
                FormatColumn(col);
            }
        }
    }

    private static void FormatColumn(ColumnBase col)
    {
        if (col == null)
            return;

        string fieldName = col.FieldName ?? string.Empty;
        string header = col.Header?.ToString() ?? string.Empty;

        // 1. Форматирование курсов валют (2 знака после запятой)
        if (fieldName.Equals("Multiplier", StringComparison.OrdinalIgnoreCase) ||
            fieldName.Equals("Divider", StringComparison.OrdinalIgnoreCase))
        {
            col.EditSettings = new SpinEditSettings
            {
                MinValue = 0,
                IsFloatValue = true,
                Mask = "n2",
                MaskType = MaskType.Numeric,
                MaskUseAsDisplayFormat = true
            };
        }

        // 2. Колонка Author в журнале склада и документах
        if (fieldName.Equals("Author", StringComparison.OrdinalIgnoreCase) ||
            fieldName.Equals("TransactionAuthor", StringComparison.OrdinalIgnoreCase) ||
            fieldName.Equals("TransactionUserName", StringComparison.OrdinalIgnoreCase) ||
            header.Equals("Author", StringComparison.OrdinalIgnoreCase) ||
            header.Equals("Автор", StringComparison.OrdinalIgnoreCase))
        {
            col.Binding = new System.Windows.Data.Binding("TransactionUserName")
            {
                TargetNullValue = "admin",
                FallbackValue = "admin",
                Mode = System.Windows.Data.BindingMode.OneWay
            };

            col.EditSettings = new TextEditSettings
            {
                NullText = "admin"
            };
        }
    }
}