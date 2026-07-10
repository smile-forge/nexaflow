using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.Svg.Loaders;
using Nexaflow.Features.Svg.ViewModels;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Svg;

/// <summary>
/// The SVG feature parses an <c>.svg</c>/<c>.svgz</c> file into a frozen WPF drawing through
/// <see cref="SvgLoader"/> (off the UI thread), reports its dimensions/viewBox/element count, and surfaces a
/// couple of read-only AI tools. These headless tests cover the loader (plain + gzipped) and the view-model's
/// load/gating/error contract; the on-screen render and pan/zoom are covered by the UI smoke.
/// </summary>
[TestClass]
public class SvgTests
{
    private static string Sample(string name) => Path.Combine(TestSampleData.Path("svg"), name);

    [TestMethod]
    [CoversNode("svg-loader")]
    public void Loader_ReadsSvg_ProducesFrozenDrawingAndMetadata()
    {
        var loaded = new SvgLoader().Load(Sample("sample.svg"));

        Assert.IsNotNull(loaded.Image);
        Assert.IsTrue(loaded.Image.IsFrozen, "the drawing must be frozen to cross to the UI thread");
        Assert.IsTrue(loaded.Bounds.Width > 0 && loaded.Bounds.Height > 0, "renders to a non-empty extent");
        Assert.AreEqual("120", loaded.Width);
        Assert.AreEqual("120", loaded.Height);
        Assert.AreEqual("0 0 120 120", loaded.ViewBox);
        Assert.AreEqual(3, loaded.ElementCount, "rect + circle + path");
    }

    [TestMethod]
    [CoversNode("svg-loader")]
    public void Loader_ReadsGzippedSvgz_SameAsSvg()
    {
        var loaded = new SvgLoader().Load(Sample("sample.svgz"));

        Assert.IsTrue(loaded.Image.IsFrozen);
        Assert.IsTrue(loaded.Bounds.Width > 0 && loaded.Bounds.Height > 0, "gzip decodes and renders");
        Assert.AreEqual("0 0 120 120", loaded.ViewBox);
        Assert.AreEqual(3, loaded.ElementCount);
    }

    [TestMethod]
    [CoversNode("svg-viewer")]
    public void GetContext_GatedUntilLoaded()
    {
        var vm = new SvgViewModel(Sample("sample.svg"));
        Assert.IsFalse(vm.IsContextReady, "context is gated until the SVG has loaded");
        StringAssert.Contains(vm.GetContext(), "still loading");
    }

    [TestMethod]
    [CoversNode("svg-viewer")]
    public async Task ViewModel_Loads_ExposesContextAndTools()
    {
        var vm = new SvgViewModel(Sample("sample.svg"));

        await vm.LoadAsync();

        Assert.IsTrue(vm.IsLoaded);
        Assert.IsTrue(vm.IsContextReady);
        Assert.IsFalse(vm.HasError, vm.ErrorMessage);
        Assert.IsNotNull(vm.Artifact);
        Assert.AreEqual("120 × 120", vm.DimensionsText);
        Assert.IsTrue(vm.HasViewBox);
        Assert.AreEqual(3, vm.ElementCount);
        StringAssert.Contains(vm.GetContext(), "SVG image");

        var toolNames = vm.GetClientTools().Select(t => t.Name).ToList();
        CollectionAssert.IsSubsetOf(new[] { "render_svg_image", "get_svg_info" }, toolNames);
    }

    [TestMethod]
    [CoversNode("svg-viewer")]
    public async Task LoadAsync_MissingFile_SetsErrorButReleasesGate()
    {
        var vm = new SvgViewModel(Path.Combine(TestSampleData.Path("svg"), "does-not-exist.svg"));

        await vm.LoadAsync();

        Assert.IsTrue(vm.IsLoaded, "the AI send gate is released even on failure");
        Assert.IsTrue(vm.IsContextReady);
        Assert.IsTrue(vm.HasError);
        StringAssert.Contains(vm.GetContext(), "failed to load");
    }

    [TestMethod]
    [CoversNode("svg-ai-tools")]
    public async Task GetSvgInfo_Tool_ReportsDimensions()
    {
        var vm = new SvgViewModel(Sample("sample.svg"));
        await vm.LoadAsync();

        var info = vm.GetClientTools().Single(t => t.Name == "get_svg_info");
        var result = await info.InvokeAsync(new JsonObject(), default);

        Assert.IsTrue(result.Success);
        StringAssert.Contains(result.ModelText, "120 × 120");
    }

    [TestMethod]
    [CoversNode("svg-ai-tools")]
    public async Task RenderTool_FailsGracefully_BeforeLoad()
    {
        var vm = new SvgViewModel(Sample("sample.svg")); // not loaded → no artifact
        var render = vm.GetClientTools().Single(t => t.Name == "render_svg_image");

        var result = await render.InvokeAsync(new JsonObject(), default);

        Assert.IsFalse(result.Success, "render reports a friendly error when nothing is loaded yet");
    }
}
