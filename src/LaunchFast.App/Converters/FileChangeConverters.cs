using System.Globalization;
using Avalonia.Data.Converters;
using LaunchFast.Core.Scaffolding;

namespace LaunchFast.App.Converters;

/// <summary>
/// Converts a <see cref="FileChange.NewContent"/> string into its individual lines
/// for the setup-wizard review code block (rendered as one <c>TextBlock</c> per line
/// so long content scrolls without wrapping mid-line).
/// </summary>
public sealed class ContentToLinesConverter : IValueConverter
{
    public static readonly ContentToLinesConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var text = value as string ?? string.Empty;
        return text.Replace("\r\n", "\n").Split('\n');
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Maps a <see cref="FileChangeKind"/> to an uppercase pill label for the review
/// list (Avalonia has no text-transform, so the label is pre-uppercased here).
/// </summary>
public sealed class FileChangeKindLabelConverter : IValueConverter
{
    public static readonly FileChangeKindLabelConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            FileChangeKind.Create => "NEW FILE",
            FileChangeKind.InsertLane => "ADD LANE",
            FileChangeKind.AddPlatformBlock => "ADD PLATFORM",
            FileChangeKind.AppendEnv => "APPEND ENV",
            _ => "CHANGE",
        };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Shortens an absolute file path to its trailing segments (up to three) for the
/// review list header, e.g. <c>…/ios/fastlane/Fastfile</c>.
/// </summary>
public sealed class ShortPathConverter : IValueConverter
{
    public static readonly ShortPathConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrEmpty(path))
            return string.Empty;

        var parts = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length <= 3)
            return string.Join('/', parts);
        return "…/" + string.Join('/', parts[^3..]);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
