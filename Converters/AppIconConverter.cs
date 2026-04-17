using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using WinXStart.Models;
using WinXStart.Services;
using WinXStart.ViewModels;

namespace WinXStart.Converters;

/// <summary>
/// Converts AppInfo to its icon ImageSource (lazy, cached).
/// Requires the MainViewModel to be set as a static resource.
/// </summary>
public class AppIconConverter : IValueConverter
{
    public static IconExtractor? IconExtractor { get; set; }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is AppInfo app && IconExtractor != null)
            return IconExtractor.GetIcon(app);
        return null;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Returns "Pin to Start" or "Unpin from Start" text based on pin state.
/// </summary>
public class PinStateConverter : IValueConverter
{
    public static MainViewModel? ViewModel { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is AppInfo app && ViewModel != null)
            return ViewModel.IsPinned(app) ? "Unpin from Start" : "Pin to Start";
        return "Pin to Start";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Converts a double to its negative half (for centering a ghost via RenderTransform).
/// </summary>
public class NegativeHalfConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double d)
            return -d / 2.0;
        return 0.0;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
