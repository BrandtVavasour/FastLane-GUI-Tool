using LaunchFast.Core.Screenshots;

namespace LaunchFast.Core.Tests;

[TestFixture]
public sealed class ScreenshotDeviceTests
{
    // ---- Label ---------------------------------------------------------------

    [Test]
    public void Label_parses_iphone_prefix()
    {
        Assert.That(ScreenshotDevice.Label("iPhone 17 Pro Max-04_map_en.png"),
            Is.EqualTo("iPhone 17 Pro Max"));
    }

    [Test]
    public void Label_stops_before_first_digit_keeping_internal_hyphens()
    {
        Assert.That(ScreenshotDevice.Label("iPad Pro 13-inch (M5)-02_register_en.png"),
            Is.EqualTo("iPad Pro 13-inch (M5)"));
    }

    [Test]
    public void Label_handles_full_path()
    {
        var path = Path.Combine("a", "b", "iPhone 16 Pro Max-01_home_en.png");
        Assert.That(ScreenshotDevice.Label(path), Is.EqualTo("iPhone 16 Pro Max"));
    }

    [Test]
    public void Label_returns_other_when_no_match()
    {
        Assert.That(ScreenshotDevice.Label("randomshot.png"), Is.EqualTo("Other"));
        Assert.That(ScreenshotDevice.Label("1.png"), Is.EqualTo("Other"));
    }

    // ---- InClass: iOS --------------------------------------------------------

    [Test]
    public void InClass_iphone_matches_iphone_files_only()
    {
        Assert.That(ScreenshotDevice.InClass("iPhone 17 Pro Max-04_map_en.png", "iPhone"), Is.True);
        Assert.That(ScreenshotDevice.InClass("iPad Pro 13-inch (M5)-02_register_en.png", "iPhone"), Is.False);
    }

    [Test]
    public void InClass_ipad_matches_ipad_files()
    {
        Assert.That(ScreenshotDevice.InClass("iPad Pro 13-inch (M5)-02_register_en.png", "iPad"), Is.True);
        Assert.That(ScreenshotDevice.InClass("iPhone 17 Pro Max-04_map_en.png", "iPad"), Is.False);
    }

    // ---- InClass: Android ----------------------------------------------------

    [Test]
    public void InClass_phone_matches_phone_folder()
    {
        var path = Path.Combine("metadata", "android", "en-US", "images", "phoneScreenshots", "1.png");
        Assert.That(ScreenshotDevice.InClass(path, "Phone"), Is.True);
        Assert.That(ScreenshotDevice.InClass(path, "Tablet"), Is.False);
    }

    [Test]
    public void InClass_tablet_matches_ten_seven_inch_folders()
    {
        var ten = Path.Combine("images", "tenInchScreenshots", "1.png");
        var seven = Path.Combine("images", "sevenInchScreenshots", "1.png");
        Assert.That(ScreenshotDevice.InClass(ten, "Tablet"), Is.True);
        Assert.That(ScreenshotDevice.InClass(seven, "Tablet"), Is.True);
        Assert.That(ScreenshotDevice.InClass(ten, "Phone"), Is.False);
    }

    // ---- InClass: unknown ----------------------------------------------------

    [Test]
    public void InClass_unknown_key_returns_true()
    {
        Assert.That(ScreenshotDevice.InClass("anything.png", "Whatever"), Is.True);
    }
}
