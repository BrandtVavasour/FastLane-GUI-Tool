using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;

namespace LaunchFast.App.Tests;

/// <summary>
/// Renders an Avalonia control inside a headless window using the real Skia
/// backend, captures the rendered frame, and writes it to a PNG artifact under
/// the test output's <c>snapshots/</c> directory for manual inspection.
///
/// Pixel-exact baselines are intentionally NOT asserted: anti-aliasing, font
/// rasterisation and emoji glyph coverage differ across machines, so a committed
/// reference image would be brittle. Instead each snapshot test asserts the frame
/// is non-null, has the expected non-zero size, AND contains more than one
/// distinct pixel colour (i.e. something actually drew, not a blank fill). The
/// PNG is emitted to the (gitignored) test output for eyeballing.
/// </summary>
static class SnapshotHarness
{
    /// <summary>Output directory for rendered PNGs, alongside the test assembly.</summary>
    public static string OutputDir { get; } =
        Path.Combine(AppContext.BaseDirectory, "snapshots");

    /// <summary>
    /// Hosts <paramref name="content"/> in a sized window with the requested theme,
    /// pumps a layout + render pass, captures the frame and saves it as
    /// <c>snapshots/{name}.png</c>. Returns the captured bitmap.
    /// </summary>
    public static Bitmap Render(
        string name,
        Control content,
        ThemeVariant theme,
        int width = 960,
        int height = 700)
    {
        var window = new Window
        {
            Width = width,
            Height = height,
            RequestedThemeVariant = theme,
            Content = content,
        };

        try
        {
            window.Show();

            // Force layout to settle and a frame to be produced.
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Loaded);
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);

            var frame = window.CaptureRenderedFrame()
                ?? throw new InvalidOperationException(
                    $"CaptureRenderedFrame returned null for '{name}'.");

            Save(name, theme, frame);
            return frame;
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Renders an already-constructed <see cref="Window"/> (e.g. a dialog that is
    /// itself a Window and cannot be nested) and saves its frame as a PNG.
    /// </summary>
    public static Bitmap RenderWindow(string name, Window window, ThemeVariant theme)
    {
        try
        {
            window.Show();

            Dispatcher.UIThread.RunJobs(DispatcherPriority.Loaded);
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);

            var frame = window.CaptureRenderedFrame()
                ?? throw new InvalidOperationException(
                    $"CaptureRenderedFrame returned null for '{name}'.");

            Save(name, theme, frame);
            return frame;
        }
        finally
        {
            window.Close();
        }
    }

    static void Save(string name, ThemeVariant theme, Bitmap frame)
    {
        Directory.CreateDirectory(OutputDir);
        var suffix = theme == ThemeVariant.Dark ? "dark" : "light";
        var path = Path.Combine(OutputDir, $"{name}.{suffix}.png");
        frame.Save(path);
    }

    /// <summary>
    /// True when the bitmap contains at least two distinct pixel values, i.e.
    /// real content rendered rather than a single flat fill. Reads the raw BGRA
    /// pixels via <see cref="WriteableBitmap"/>.
    /// </summary>
    public static bool HasDrawnContent(Bitmap frame)
    {
        if (frame is not WriteableBitmap wb)
            return frame.PixelSize.Width > 0 && frame.PixelSize.Height > 0;

        using var locked = wb.Lock();
        var bytes = locked.RowBytes * locked.Size.Height;
        var buffer = new byte[bytes];
        Marshal.Copy(locked.Address, buffer, 0, bytes);

        // 4 bytes per pixel (BGRA). Compare every pixel to the first; any
        // difference means more than a single flat colour was painted.
        uint first = BitConverter.ToUInt32(buffer, 0);
        for (var i = 4; i + 4 <= buffer.Length; i += 4)
        {
            if (BitConverter.ToUInt32(buffer, i) != first)
                return true;
        }
        return false;
    }
}
