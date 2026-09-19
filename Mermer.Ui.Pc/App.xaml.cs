using System;
using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Markup;
using DevExpress.Xpf.Grid;
using DevExpress.Xpf.Editors;
using DevExpress.Xpf.Editors.Settings;

namespace Mermer.Ui.Pc
{
    public partial class App : Application
    {
        public App()
        {
            // Берем культуру динамически из настроек текущей системы Windows:
            var culture = CultureInfo.CurrentCulture;
            Thread.CurrentThread.CurrentCulture = culture;
            Thread.CurrentThread.CurrentUICulture = CultureInfo.CurrentUICulture;

            FrameworkElement.LanguageProperty.OverrideMetadata(
                typeof(FrameworkElement),
                new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(culture.IetfLanguageTag)));

            // 1. Перехват колонок при их автоматической генерации гридом
            EventManager.RegisterClassHandler(
                typeof(GridControl),
                GridControl.AutoGeneratingColumnEvent,
                new AutoGeneratingColumnEventHandler(OnGridAutoGeneratingColumn)
            );

            // 2. Перехват колонок при загрузке любого грида
            EventManager.RegisterClassHandler(
                typeof(GridControl),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler(OnGridLoaded)
            );
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
                // Привязываем к полю модели с автоматической подстановкой admin
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
}