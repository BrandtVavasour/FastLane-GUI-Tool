using System.Collections;
using System.Globalization;
using Avalonia.Data.Converters;

namespace LaunchFast.App.Converters;

/// <summary>
/// Multi-value converter: given the active <c>StepIndex</c>, the <c>StepTitles</c>
/// list and a <c>ConverterParameter</c> title, returns whether the current step's
/// title equals the parameter. The setup-wizard footer uses this to show the
/// "Generate" button only on the Review step. A <c>ConverterParameter</c> prefixed
/// with <c>!</c> negates the result (used to show "Next" on every step except Review).
/// </summary>
public sealed class CurrentStepTitleEqualsConverter : IMultiValueConverter
{
    public static readonly CurrentStepTitleEqualsConverter Instance = new();

    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (parameter is not string raw)
            return false;

        var negate = raw.StartsWith('!');
        var expected = negate ? raw[1..] : raw;

        var match = values.Count >= 2
            && values[0] is int index
            && values[1] is IList titles
            && index >= 0 && index < titles.Count
            && Equals(titles[index], expected);

        return negate ? !match : match;
    }
}
