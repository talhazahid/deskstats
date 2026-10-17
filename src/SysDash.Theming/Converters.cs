using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SysDash.Theming.Converters;

/// <summary>bool → Visibility. Options: Invert flips the input; Hidden substitutes Hidden for Collapsed.</summary>
public class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public bool Hidden { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool visible = value is bool b && b;
        if (Invert)
        {
            visible = !visible;
        }

        return visible ? Visibility.Visible : (Hidden ? Visibility.Hidden : Visibility.Collapsed);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>bool → Visibility, inverted (true → Collapsed).</summary>
public sealed class InverseBoolToVisibilityConverter : BoolToVisibilityConverter
{
    public InverseBoolToVisibilityConverter()
    {
        Invert = true;
    }
}

/// <summary>null or empty string → Collapsed, otherwise Visible.</summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is null || (value is string s && s.Length == 0) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Reverses a boolean.</summary>
public sealed class InvertBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : value;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Convert(value, targetType, parameter, culture);
}

/// <summary>bool → double opacity (true → 1.0).</summary>
public sealed class BoolToOpacityConverter : IValueConverter
{
    /// <summary>Opacity used when the value is false.</summary>
    public double FalseOpacity { get; set; } = 0.45;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && b ? 1.0 : FalseOpacity;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>String formatting passthrough: parameter is the {0:...} format string.</summary>
public sealed class StringFormatConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is null || parameter is not string format || format.Length == 0)
        {
            return value ?? string.Empty;
        }

        try
        {
            return string.Format(culture, format, value);
        }
        catch (FormatException)
        {
            return value ?? string.Empty;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}