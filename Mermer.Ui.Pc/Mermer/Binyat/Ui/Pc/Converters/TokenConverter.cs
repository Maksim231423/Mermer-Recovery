using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Data;
using System.Windows.Markup;

namespace Mermer.Ui.Pc.Converters;

public class TokenConverter : MarkupExtension, IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var strings = new List<string>();

        if (value is IEnumerable enumerable && !(value is string))
        {
            strings = enumerable.Cast<object>()
                                .Select(x => x?.ToString())
                                .Where(x => !string.IsNullOrWhiteSpace(x))
                                .ToList();
        }
        else if (value is string str && !string.IsNullOrWhiteSpace(str))
        {
            strings.Add(str);
        }

        // Если запрашивает TextBlock в списке (нужна строка)
        if (targetType == typeof(string))
        {
            return strings.Count > 0 ? string.Join(", ", strings) : null;
        }

        // Если элементов нет — ОБЯЗАТЕЛЬНО возвращаем null, чтобы скрыть крестик очистки
        if (strings.Count == 0)
        {
            return null;
        }

        // Если запрашивает редактор токенов (нужна коллекция List<object>)
        return strings.Cast<object>().ToList();
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null)
        {
            return new List<string>();
        }

        // Превращаем токены обратно в список строк для сохранения в базу
        if (value is IEnumerable<object> objectSource)
        {
            return objectSource.Select(x => x?.ToString())
                               .Where(x => !string.IsNullOrWhiteSpace(x))
                               .ToList();
        }

        if (value is string str && !string.IsNullOrWhiteSpace(str))
        {
            return str.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                      .Select(x => x.Trim())
                      .Where(x => !string.IsNullOrWhiteSpace(x))
                      .ToList();
        }

        return new List<string>();
    }

    public override object ProvideValue(IServiceProvider serviceProvider) => this;
}