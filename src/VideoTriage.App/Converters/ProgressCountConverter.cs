using System;
using System.Globalization;
using System.Windows.Data;

namespace VideoTriage.App.Converters;

/// <summary>
/// Multiplies an animated 0..1 progress by a total count and rounds, so a
/// label can "count up" in lock-step with an animation. Inputs: [0] progress
/// (double), [1] total (int). Falls back to the raw total if inputs are missing.
/// </summary>
public sealed class ProgressCountConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        var total = values is { Length: > 1 } && values[1] is int t ? t : 0;
        var progress = values is { Length: > 0 } && values[0] is double p ? p : 1.0;
        if (progress < 0) progress = 0;
        if (progress > 1) progress = 1;
        return ((int)Math.Round(progress * total)).ToString(culture);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
