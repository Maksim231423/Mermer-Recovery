using System;
using System.Globalization;
using System.Windows.Data;

namespace Mermer.Ui.Pc.Converters
{
    public class PasswordDisplayConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // Если из модели прилетает Base64-хэш SHA-1/SHA-256 (заканчивается на = и длина 28 или 44)
            if (value is string str && str.EndsWith("=") && (str.Length == 28 || str.Length == 44))
            {
                return string.Empty; // Очищаем поле ввода, чтобы показывался NullText
            }

            return value ?? string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value;
        }
    }
}