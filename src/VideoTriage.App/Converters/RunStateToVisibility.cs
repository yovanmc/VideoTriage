using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using VideoTriage.App.ViewModels;

namespace VideoTriage.App.Converters;

public sealed class RunStateToVisibility : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not RunState state || parameter is not string param)
        {
            return Visibility.Collapsed;
        }

        var matches = param switch
        {
            "Idle" => state == RunState.Idle,
            "Running" => state == RunState.Running,
            "Paused" => state == RunState.Paused,
            "RunningOrPaused" => state is RunState.Running or RunState.Paused,
            "Active" => state is RunState.Running or RunState.Paused or RunState.Stopping,
            _ => false,
        };

        return matches ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
