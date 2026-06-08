using LaunchFast.App.ViewModels;
using LaunchFast.Core.Models;
using LaunchFast.Core.Scanning;
using LaunchFast.Core.Updates;

namespace LaunchFast.App.Tests;

public class LauncherUpdateBannerTests
{
    static LauncherViewModel MakeLauncher()
    {
        var storeFile = Path.GetTempFileName();
        var store = new ProjectStore(storeFile);
        return new LauncherViewModel(store);
    }

    [Test]
    public void No_update_by_default()
    {
        var vm = MakeLauncher();
        Assert.That(vm.HasUpdate, Is.False);
    }

    [Test]
    public void Setting_update_exposes_banner_text_and_url()
    {
        var vm = MakeLauncher();
        vm.SetAvailableUpdate(new ReleaseInfo("v0.2.0", "https://x/releases/tag/v0.2.0"));

        Assert.That(vm.HasUpdate, Is.True);
        Assert.That(vm.UpdateBannerText, Does.Contain("v0.2.0"));
        Assert.That(vm.UpdateUrl, Is.EqualTo("https://x/releases/tag/v0.2.0"));
    }
}
