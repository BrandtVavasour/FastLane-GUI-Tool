using LaunchFast.Core.Screenshots;

namespace LaunchFast.Core.Tests;

[TestFixture]
public sealed class DeviceClassTests
{
    // ---- iPhone 6.9" class (Pro Max / Plus, current hardware) ----------------

    [TestCase("iPhone 17 Pro Max")]
    [TestCase("iPhone 16 Pro Max")]
    [TestCase("iPhone 15 Plus")]
    public void Classify_maps_current_large_iphones_to_69(string device)
    {
        Assert.That(DeviceClass.Classify(device), Is.EqualTo(DeviceClass.IPhone69));
    }

    // ---- iPhone 6.5" class ---------------------------------------------------

    [TestCase("iPhone 11 Pro Max")]
    [TestCase("iPhone XS Max")]
    public void Classify_maps_older_max_iphones_to_65(string device)
    {
        Assert.That(DeviceClass.Classify(device), Is.EqualTo(DeviceClass.IPhone65));
    }

    // ---- iPhone 5.5" class ---------------------------------------------------

    [Test]
    public void Classify_maps_8_plus_to_55()
    {
        Assert.That(DeviceClass.Classify("iPhone 8 Plus"), Is.EqualTo(DeviceClass.IPhone55));
    }

    // ---- iPad 13" class ------------------------------------------------------

    [TestCase("iPad Pro 13-inch (M5)")]
    [TestCase("iPad Pro (12.9-inch)")]
    public void Classify_maps_large_ipads_to_13(string device)
    {
        Assert.That(DeviceClass.Classify(device), Is.EqualTo(DeviceClass.IPad13));
    }

    // ---- iPad 11" class ------------------------------------------------------

    [Test]
    public void Classify_maps_11_inch_ipad_to_11()
    {
        Assert.That(DeviceClass.Classify("iPad Pro (11-inch)"), Is.EqualTo(DeviceClass.IPad11));
    }

    // ---- unknown / empty -----------------------------------------------------

    [TestCase("iPhone SE")]
    [TestCase("Other")]
    [TestCase("")]
    public void Classify_returns_null_for_unknown(string device)
    {
        Assert.That(DeviceClass.Classify(device), Is.Null);
    }

    [Test]
    public void Classify_is_case_insensitive()
    {
        Assert.That(DeviceClass.Classify("IPHONE 17 PRO MAX"), Is.EqualTo(DeviceClass.IPhone69));
    }
}
