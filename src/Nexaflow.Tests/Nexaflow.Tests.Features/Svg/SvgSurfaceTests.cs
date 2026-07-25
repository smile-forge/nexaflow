using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Nexaflow.Features.Common;
using Nexaflow.Features.Svg.FileActions;
using Nexaflow.Features.Svg.ViewModels;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Features.Svg;

/// <summary>
/// The SVG tab's own surfaces: the footer's metadata line, the checkerboard toggle, the error overlay, and
/// the gate that holds the AI back until there is something true to say.
/// <para>
/// The footer is the only place the file's declared size and viewBox are shown, and both matter more here
/// than in a bitmap viewer: a vector file's <c>width</c> attribute and its <c>viewBox</c> can disagree, and
/// which one you are looking at decides whether the art is what its author intended.
/// </para>
/// </summary>
[TestClass]
public class SvgSurfaceTests
{
    private static string Sample(string name) => Path.Combine(TestSampleData.Path("svg"), name);

    private static async Task<SvgViewModel> LoadedAsync(string name = "sample.svg")
    {
        var vm = new SvgViewModel(Sample(name));
        await vm.LoadAsync();
        return vm;
    }

    // ── Metadata footer ───────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("svg-footer")]
    public async Task TheFooterReportsTheDeclaredSize_TheViewBoxAndTheElementCount()
    {
        var vm = await LoadedAsync();

        Assert.AreEqual("120 × 120", vm.DimensionsText, "the file's own width/height, not the render bounds");
        Assert.IsTrue(vm.HasViewBox);
        Assert.AreEqual("0 0 120 120", vm.ViewBoxText);
        Assert.AreEqual(3, vm.ElementCount, "rect + circle + path");
        Assert.IsFalse(string.IsNullOrEmpty(vm.FileSizeText));
    }

    [TestMethod]
    [CoversNode("svg-footer")]
    public async Task AFileWithoutAViewBoxHidesThatPartOfTheFooter()
    {
        // An SVG need not declare a viewBox; showing "viewBox " with nothing after it is worse than nothing.
        var path = Path.Combine(Path.GetTempPath(), $"svgnovb_{Guid.NewGuid():N}.svg");
        File.WriteAllText(path,
            """<svg xmlns="http://www.w3.org/2000/svg" width="40" height="20"><rect width="40" height="20"/></svg>""");
        try
        {
            var vm = new SvgViewModel(path);
            await vm.LoadAsync();

            Assert.IsFalse(vm.HasViewBox);
            Assert.AreEqual(string.Empty, vm.ViewBoxText);
            Assert.AreEqual("40 × 20", vm.DimensionsText, "the declared size still shows");
        }
        finally { try { File.Delete(path); } catch { } }
    }

    // ── Checkerboard toggle ───────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("svg-checkerboard")]
    public async Task TheCheckerboardIsOnByDefault_AndCanBeTurnedOff()
    {
        var vm = await LoadedAsync();

        Assert.IsTrue(vm.ShowCheckerboard,
                      "vector art is usually transparent — on a plain background you cannot tell " +
                      "transparent from white");

        vm.ShowCheckerboard = false;

        Assert.IsFalse(vm.ShowCheckerboard, "and off again, for judging the art against a flat backdrop");
    }

    // ── Error overlay + the send gate ─────────────────────────────────────────

    [TestMethod]
    [CoversNode("svg-error-overlay")]
    public async Task AFileThatCannotBeReadShowsWhy_RatherThanAnEmptyCanvas()
    {
        var vm = new SvgViewModel(Sample("does-not-exist.svg"));

        await vm.LoadAsync();

        Assert.IsTrue(vm.HasError);
        Assert.IsFalse(string.IsNullOrWhiteSpace(vm.ErrorMessage));
        Assert.IsNull(vm.Artifact);
    }

    [TestMethod]
    [CoversNode("svg-error-overlay")]
    public async Task AFileThatParsesButDrawsNothingIsAlsoAnError()
    {
        // Valid XML, valid SVG, nothing in it. Without this the tab is a blank canvas with no explanation,
        // which reads as "the viewer is broken" rather than "the file is empty".
        var path = Path.Combine(Path.GetTempPath(), $"svgempty_{Guid.NewGuid():N}.svg");
        File.WriteAllText(path, """<svg xmlns="http://www.w3.org/2000/svg" width="0" height="0"></svg>""");
        try
        {
            var vm = new SvgViewModel(path);
            await vm.LoadAsync();

            Assert.IsTrue(vm.HasError);
            StringAssert.Contains(vm.ErrorMessage, "nothing to render");
        }
        finally { try { File.Delete(path); } catch { } }
    }

    [TestMethod]
    [CoversNode("svg-load-gate")]
    public void TheAiIsHeldBackUntilTheFileHasLoaded()
    {
        var vm = new SvgViewModel(Sample("sample.svg"));

        Assert.IsFalse(vm.IsContextReady, "pinning the tab the instant it opens must not send an empty page");
        StringAssert.Contains(vm.GetContext(), "still loading");
    }

    [TestMethod]
    [CoversNode("svg-load-gate")]
    public async Task TheGateIsReleasedEvenWhenTheLoadFails()
    {
        // Otherwise a broken file leaves the conversation waiting forever for a page that will never load.
        var vm = new SvgViewModel(Sample("does-not-exist.svg"));

        await vm.LoadAsync();

        Assert.IsTrue(vm.IsContextReady);
        StringAssert.Contains(vm.GetContext(), "failed to load");
    }

    [TestMethod]
    [CoversNode("svg-load-gate")]
    public async Task LoadingTwiceIsANoOp()
    {
        var vm = await LoadedAsync();
        var first = vm.Artifact;

        await vm.LoadAsync();

        Assert.AreSame(first, vm.Artifact, "re-entering the view must not re-parse and re-render the file");
    }
}

/// <summary>
/// "As SVG" — the file action that opens a vector file in the viewer.
/// </summary>
[TestClass]
[CoversNode("svg-open-actions")]
public class SvgOpenActionTests
{
    private static (IShellServices Shell, List<Dictionary<string, string>> Opened) Shell()
    {
        var shell = Substitute.For<IShellServices>();
        var opened = new List<Dictionary<string, string>>();
        shell.When(s => s.OpenTab(Arg.Any<string>(), Arg.Any<Dictionary<string, string>>()))
             .Do(ci => opened.Add(ci.ArgAt<Dictionary<string, string>>(1)));
        return (shell, opened);
    }

    [TestMethod]
    public void ItOpensTheFileItWasInvokedOn()
    {
        var (shell, opened) = Shell();

        Assert.IsTrue(new ShowSvgAction(shell).PerformAction(@"C:\art\logo.svg"));

        Assert.AreEqual(@"C:\art\logo.svg", opened.Single()["path"]);
    }

    [TestMethod]
    public void ItIsAViewer_AndChangesNothing()
    {
        var action = new ShowSvgAction(Substitute.For<IShellServices>());

        Assert.IsTrue(action.OpensViewer);
        Assert.IsFalse(action.IsDestructive);
    }

    [TestMethod]
    public void AnEmptySelectionOpensNothing()
    {
        var (shell, opened) = Shell();

        Assert.IsFalse(new ShowSvgAction(shell).PerformAction([]));

        Assert.AreEqual(0, opened.Count);
    }
}
