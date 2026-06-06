using System.Collections;
using System.Globalization;
using Avalonia.Data.Converters;

namespace LaunchFast.App.Converters;

/// <summary>
/// Multi-value converter for the setup-wizard step rail: given the rail item's title
/// and the <c>StepTitles</c> list, returns the item's 1-based position as a string
/// (e.g. "1", "2", …) for the numbered step circle.
/// </summary>
public sealed class StepNumberConverter : IMultiValueConverter
{
    public static readonly StepNumberConverter Instance = new();

    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 2 || values[0] is not string title || values[1] is not IList titles)
            return string.Empty;

        var i = titles.IndexOf(title);
        return i < 0 ? string.Empty : (i + 1).ToString(CultureInfo.InvariantCulture);
    }
}
