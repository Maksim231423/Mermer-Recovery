using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;

namespace Mermer.Ui.Pc.Converters;

public class InvoiceTypeConverter : MarkupExtension, IValueConverter
{
    public override object ProvideValue(IServiceProvider serviceProvider) => this;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null)
            return string.Empty;

        var valStr = value.ToString();

        return valStr switch
        {
            "0" or "Purchase" => "Purchase",        // Закупка
            "1" or "PurchaseReturn" => "Purchase Return", // Возврат поставщику
            "2" or "Sales" => "Sales",           // Продажа
            "3" or "SalesReturn" => "Sales Return",    // Возврат от покупателя
            "4" or "Transfer" => "Transfer",        // Перемещение
            "5" or "Loss" => "Loss / Write-off",// Списание
            "6" or "Income" => "Surplus",         // Оприходование
            "7" or "Repricing" => "Repricing",       // Переоценка
            _ => valStr
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}