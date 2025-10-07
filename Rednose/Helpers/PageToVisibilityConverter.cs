using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace APR_Rednose.Helpers
{
    public sealed class PageToVisibilityConverter : IValueConverter
    {
        // ✅ Convert는 반드시 1개만
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var current = value as string;
            var target = parameter as string;

            if (!string.IsNullOrEmpty(current) &&
                !string.IsNullOrEmpty(target) &&
                string.Equals(current, target, StringComparison.OrdinalIgnoreCase))
            {
                return Visibility.Visible;
            }

            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
