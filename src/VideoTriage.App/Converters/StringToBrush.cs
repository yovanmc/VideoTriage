using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace VideoTriage.App.Converters;

public sealed class StringToBrush : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string s || string.IsNullOrEmpty(s))
            return Brushes.Transparent;

        try
        {
            var color = (Color)ColorConverter.ConvertFromString(s);
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
        catch
        {
            return Brushes.Transparent;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
