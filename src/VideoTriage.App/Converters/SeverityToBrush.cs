using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using VideoTriage.App.ViewModels;

namespace VideoTriage.App.Converters;

public sealed class SeverityToBrush : IValueConverter
{
    private static readonly SolidColorBrush SuccessBrush = CreateFrozen(Color.FromArgb(0x33, 0x5A, 0xD1, 0x7F));
    private static readonly SolidColorBrush WarningBrush = CreateFrozen(Color.FromArgb(0x33, 0xE8, 0xC3, 0x5A));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            SummarySeverity.Success => SuccessBrush,
            SummarySeverity.Warning => WarningBrush,
            _ => Brushes.Transparent,
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static SolidColorBrush CreateFrozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
