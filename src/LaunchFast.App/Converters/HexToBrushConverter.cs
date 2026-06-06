using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace LaunchFast.App.Converters;

/// <summary>
/// Converts a hex colour string (e.g. <c>"#1E8E64"</c>) to a <see cref="SolidColorBrush"/>.
/// Used by the Screenshots frameit swatches and framed-preview composition to tint
/// placeholder backgrounds. Falls back to a fixed green for an unparseable value;
/// never throws.
/// </summary>
public sealed class HexToBrushConverter : IValueConverter
{
    public static readonly HexToBrushConverter Instance = new();

    static readonly SolidColorBrush Fallback = new(Color.Parse("#1E8E64"));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string hex && !string.IsNullOrWhiteSpace(hex))
        {
            try { return new SolidColorBrush(Color.Parse(hex)); }
            catch { return Fallback; }
        }
        return Fallback;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
