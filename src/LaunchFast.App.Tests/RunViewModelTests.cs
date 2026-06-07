using LaunchFast.App.ViewModels;

namespace LaunchFast.App.Tests;

public class RunViewModelTests
{
    [Test]
    public void HasLines_is_false_when_empty_and_true_once_a_line_is_appended()
    {
        var vm = new RunViewModel();
        Assert.That(vm.HasLines, Is.False);

        vm.Lines.Add("Running lane…");
        Assert.That(vm.HasLines, Is.True);
    }

    [Test]
    public void AllText_joins_lines_with_newlines()
    {
        var vm = new RunViewModel();
        vm.Lines.Add("line one");
        vm.Lines.Add("line two");

        Assert.That(vm.AllText, Is.EqualTo("line one\nline two"));
    }

    [Test]
    public void Clearing_lines_notifies_and_resets_HasLines()
    {
        var vm = new RunViewModel();
        vm.Lines.Add("output");

        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.Lines.Clear();

        Assert.That(vm.HasLines, Is.False);
        Assert.That(vm.AllText, Is.Empty);
        Assert.That(changed, Does.Contain(nameof(RunViewModel.HasLines)));
        Assert.That(changed, Does.Contain(nameof(RunViewModel.AllText)));
    }
}
