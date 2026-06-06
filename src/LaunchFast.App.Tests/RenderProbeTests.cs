using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Headless.NUnit;
using Avalonia.Styling;

namespace LaunchFast.App.Tests;

/// <summary>
/// Probe: can this environment render a real frame (Skia headless) rather than
/// the no-op headless drawing backend? Decides whether the snapshot suite captures
/// pixels or just exercises load+bind+layout. The probe asserts a sized frame is
/// produced AND that it contains more than a single flat colour, proving Skia
/// actually rasterised content.
/// </summary>
public class RenderProbeTests
{
    [AvaloniaTest]
    public void Headless_renders_a_nonempty_frame_with_real_pixels()
    {
        var content = new Border
        {
            Background = Brushes.CornflowerBlue,
            Child = new TextBlock { Text = "probe", Foreground = Brushes.White },
        };

        var frame = SnapshotHarness.Render("_probe", content, ThemeVariant.Light, 200, 100);

        Assert.That(frame, Is.Not.Null, "CaptureRenderedFrame returned null");
        Assert.That(frame.Size.Width, Is.GreaterThan(0));
        Assert.That(frame.Size.Height, Is.GreaterThan(0));
        Assert.That(SnapshotHarness.HasDrawnContent(frame), Is.True,
            "Frame was a single flat colour — Skia did not rasterise real content.");
    }
}
