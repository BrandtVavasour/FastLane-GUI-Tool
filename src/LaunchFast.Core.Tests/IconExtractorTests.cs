using LaunchFast.Core.Icons;

public class IconExtractorTests
{
    [Test]
    public void Picks_largest_ios_icon()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var iconset = Path.Combine(root, "ios", "Runner", "Assets.xcassets", "AppIcon.appiconset");
        Directory.CreateDirectory(iconset);
        File.WriteAllBytes(Path.Combine(iconset, "Icon-20.png"), new byte[100]);
        File.WriteAllBytes(Path.Combine(iconset, "Icon-1024.png"), new byte[5000]);

        var path = IconExtractor.Resolve(root);
        Assert.That(Path.GetFileName(path), Is.EqualTo("Icon-1024.png"));
    }

    [Test]
    public void Returns_null_when_no_icon()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Assert.That(IconExtractor.Resolve(root), Is.Null);
    }

    [Test]
    public void Picks_android_icon_when_no_ios()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var mipmapDir = Path.Combine(root, "android", "app", "src", "main", "res", "mipmap-hdpi");
        Directory.CreateDirectory(mipmapDir);
        var iconPath = Path.Combine(mipmapDir, "ic_launcher.png");
        File.WriteAllBytes(iconPath, new byte[200]);

        var path = IconExtractor.Resolve(root);
        Assert.That(Path.GetFileName(path), Is.EqualTo("ic_launcher.png"));
    }
}
