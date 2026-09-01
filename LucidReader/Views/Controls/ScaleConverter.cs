using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace LucidReader.Views.Controls;

/// <summary>
/// Multiplies a bound double by the factor given as ConverterParameter.
///
/// Exists because the hero image is sized as a fraction of the window rather
/// than in fixed pixels, and Avalonia has no equivalent of CSS's vw and vh
/// units. Binding MaxWidth to the window's own Bounds.Width through this
/// gives the same thing: a cap that follows the window instead of a number
/// that is right at one size and wrong at every other.
///
/// Unset rather than zero for a value that is not a usable number. A window
/// reports NaN for its bounds before its first layout pass, and returning 0
/// for that would set MaxWidth to zero and collapse the image permanently,
/// since a MaxWidth of 0 is a real constraint rather than an absent one.
/// UnsetValue leaves the property at its default until a real measurement
/// arrives.
/// </summary>
public sealed class ScaleConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not double measurement
            || double.IsNaN(measurement)
            || double.IsInfinity(measurement))
            return AvaloniaProperty.UnsetValue;

        var factor = parameter switch
        {
            double direct => direct,
            // Parsed invariant, not with the current culture: the factor is
            // written in the XAML as "0.5" and stays "0.5" on a machine whose
            // decimal separator is a comma.
            string text when double.TryParse(
                text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => 1.0
        };

        return Math.Max(0, measurement * factor);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("ScaleConverter is one-way.");
}
