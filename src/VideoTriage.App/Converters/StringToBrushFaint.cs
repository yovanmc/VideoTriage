using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace VideoTriage.App.Converters;

public sealed class StringToBrushFaint : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string s || string.IsNullOrEmpty(s))
            return Brushes.Transparent;

        try
        {
            var color = (Color)ColorConverter.ConvertFromString(s);
            var faint = Color.FromArgb(0x33, color.R, color.G, color.B);
            var brush = new SolidColorBrush(faint);
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
