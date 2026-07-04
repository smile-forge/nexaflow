using System.Linq;
using Nexaflow.Features.Common;
using Nexaflow.Features.Text.ViewModels;
using NSubstitute;

namespace Nexaflow.Tests.Features.Text;

/// <summary>
/// Headless command/state logic behind the Text toolbar &amp; split panel — the view toggles and
/// split-mode wiring that need no file load and no AvalonEdit <c>TextDocument</c> mutation. The
/// windowed reader is covered by <see cref="TextViewModelTests"/>; the interactive controls are
/// exercised end-to-end by the UI journey. Everything here runs synchronously on the test thread.
/// </summary>
[TestClass]
public class TextViewModelCommandTests
{
    private static TextViewModel Make()
        => new("nonexistent.txt", Substitute.For<IShellServices>()) { IsMonitoring = false };

    [TestMethod]
    public void ToggleSplitPanel_FlipsOpenState()
    {
        using var vm = Make();
        Assert.IsFalse(vm.IsSplitPanelOpen);

        vm.ToggleSplitPanelCommand.Execute(null);
        Assert.IsTrue(vm.IsSplitPanelOpen);

        vm.ToggleSplitPanelCommand.Execute(null);
        Assert.IsFalse(vm.IsSplitPanelOpen);
    }

    [TestMethod]
    public void SelectedSplitMode_ByLineCount_SetsLineHintAndDefault()
    {
        using var vm = Make();
        var byLines = vm.SplitModes.First(m => m.Label == "By line count");

        vm.SelectedSplitMode = byLines;

        Assert.AreEqual("Lines per file", vm.SplitValueHint);
        Assert.AreEqual("1000", vm.SplitValue);
    }

    [TestMethod]
    public void SelectedSplitMode_BySize_SetsMegabyteHintAndDefault()
    {
        using var vm = Make();
        var bySize = vm.SplitModes.First(m => m.Label == "By size (MB)");

        vm.SelectedSplitMode = bySize;

        Assert.AreEqual("Megabytes per file", vm.SplitValueHint);
        Assert.AreEqual("10", vm.SplitValue);
    }

    [TestMethod]
    public void SelectedSplitMode_ByRegex_SetsRegexHintAndDefault()
    {
        using var vm = Make();
        var byRegex = vm.SplitModes.First(m => m.Label == "At lines matching regex");

        vm.SelectedSplitMode = byRegex;

        Assert.AreEqual("Regex — new file at each match", vm.SplitValueHint);
        Assert.AreEqual("^", vm.SplitValue);
    }

    [TestMethod]
    public void ToggleMonitoring_FlipsFlag_AndIsSafeWithoutFile()
    {
        using var vm = Make();   // path doesn't exist → StartMonitoring is a guarded no-op
        Assert.IsFalse(vm.IsMonitoring);

        vm.ToggleMonitoringCommand.Execute(null);
        Assert.IsTrue(vm.IsMonitoring);

        vm.ToggleMonitoringCommand.Execute(null);
        Assert.IsFalse(vm.IsMonitoring);
    }

    [TestMethod]
    public void DisplayToggles_DefaultToLineNumbersOnAndWrapOff()
    {
        using var vm = Make();
        Assert.IsTrue(vm.ShowLineNumbers);
        Assert.IsFalse(vm.WordWrap);

        vm.ShowLineNumbers = false;
        vm.WordWrap        = true;

        Assert.IsFalse(vm.ShowLineNumbers);
        Assert.IsTrue(vm.WordWrap);
    }

    [TestMethod]
    public void CancelSearch_ResetsSearchState()
    {
        using var vm = Make();

        vm.CancelSearchCommand.Execute(null);

        Assert.IsFalse(vm.IsSearchActive);
        Assert.AreEqual(0, vm.SearchMatchCount);
        Assert.AreEqual(string.Empty, vm.CurrentSearchTerm);
        Assert.AreEqual(-1, vm.ScrollToOffset);
        Assert.AreEqual(0, vm.SearchHighlights.Count);
    }
}
