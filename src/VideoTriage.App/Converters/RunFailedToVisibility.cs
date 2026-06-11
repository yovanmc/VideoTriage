using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace VideoTriage.App.Converters;

public sealed class RunFailedToVisibility : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string message && message.StartsWith("Run failed", StringComparison.OrdinalIgnoreCase)
            ? Visibility.Visible
            : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
