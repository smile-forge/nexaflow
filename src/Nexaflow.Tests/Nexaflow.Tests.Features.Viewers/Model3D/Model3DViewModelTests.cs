using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.Model3D.Loaders;
using Nexaflow.Features.Model3D.ViewModels;
using Nexaflow.Features.Model3D.Views;
using Nexaflow.Tests.Features.Infrastructure;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Common.Previews;

namespace Nexaflow.Tests.Features.Model3D;

/// <summary>
/// Headless command/state logic behind the Model3D toolbar &amp; AI tools. The render-mode + inspector
/// state and the <c>set_render_mode</c> tool run without a viewport; the camera/capture tools need the live
/// <c>HelixViewport3D</c> (wired in by the view) and are covered by the UI journey instead — here they're
/// only checked to fail gracefully when no viewport is attached. Loader/mesh/material parsing and the
/// post-load happy path live in <see cref="Model3DTests"/>; this file does not duplicate them.
/// </summary>
[TestClass]
public class Model3DViewModelTests
{
    private static Model3DViewModel Make() => new("model.stl", new ModelLoaderRegistry());

    [TestMethod]
    [CoversNode("model3d-wireframe")]
    public void ShowWireframe_DefaultsSolid_AndToggles()
    {
        var vm = Make();
        Assert.IsFalse(vm.ShowWireframe, "defaults to solid shading");

        vm.ShowWireframe = true;
        Assert.IsTrue(vm.ShowWireframe);
        vm.ShowWireframe = false;
        Assert.IsFalse(vm.ShowWireframe);
    }

    [TestMethod]
    [CoversNode("model3d-wireframe")]
    public void DescribeInfo_ReflectsRenderMode()
    {
        var vm = Make();
        StringAssert.Contains(vm.DescribeInfo(), "Render mode: solid.");

        vm.ShowWireframe = true;
        StringAssert.Contains(vm.DescribeInfo(), "Render mode: wireframe.");
    }

    [TestMethod]
    [CoversNode("model3d-inspector-toggle")]
    public void ShowInspector_IsIndependentObservableState()
    {
        var vm = Make();
        Assert.IsFalse(vm.ShowInspector, "no inspector content before a model loads");

        vm.ShowInspector = true;
        Assert.IsTrue(vm.ShowInspector);
        vm.ShowInspector = false;
        Assert.IsFalse(vm.ShowInspector);
    }

    [TestMethod]
    [CoversNode("model3d-ai-context")]
    public void GetContext_GatedUntilLoaded()
    {
        var vm = Make();
        Assert.IsFalse(vm.IsContextReady, "context is gated until the model has loaded");
        StringAssert.Contains(vm.GetContext(), "still loading");
    }

    [TestMethod]
    [CoversNode("model3d-ai-act")]
    [CoversNode("model3d-wireframe")]
    public async Task SetRenderMode_Tool_TogglesAndSetsWireframe()
    {
        var vm = Make();
        var tool = Tool(vm, "set_render_mode");

        // No argument → toggles from the current (solid) state.
        await tool.InvokeAsync(new JsonObject(), default);
        Assert.IsTrue(vm.ShowWireframe);

        // Explicit false → solid.
        await tool.InvokeAsync(new JsonObject { ["wireframe"] = false }, default);
        Assert.IsFalse(vm.ShowWireframe);

        // Explicit true → wireframe.
        await tool.InvokeAsync(new JsonObject { ["wireframe"] = true }, default);
        Assert.IsTrue(vm.ShowWireframe);
    }

    [TestMethod]
    [CoversNode("model3d-ai-act")]
    public async Task CameraTools_FailGracefully_WithoutViewport()
    {
        var vm = Make(); // no view attached → camera/capture delegates are null

        foreach (var name in new[] { "orbit_model", "roll_model", "zoom_model", "pan_model", "reset_view", "capture_viewport_image" })
        {
            var result = await Tool(vm, name).InvokeAsync(new JsonObject(), default);
            Assert.IsFalse(result.Success, $"'{name}' should report a friendly error when no viewport is wired.");
        }
    }

    [TestMethod]
    [CoversNode("model3d-ai-act-get-model-info")]
    public async Task GetModelInfo_Tool_ReportsLoadedMeshStats()
    {
        // Mesh parsing needs no viewport, so a real sample loads headlessly and the tool can
        // report exact facts instead of the empty pre-load state.
        var vm = new Model3DViewModel(
            Path.Combine(TestSampleData.Path("model3d"), "tetra.stl"),
            new ModelLoaderRegistry());
        await vm.LoadAsync();
        Assert.IsFalse(vm.HasError, vm.ErrorMessage);

        var result = await Tool(vm, "get_model_info").InvokeAsync(new JsonObject(), default);

        Assert.IsFalse(result.IsError, "get_model_info reads the loaded model — it never fails.");
        Assert.IsTrue(result.Success);
        StringAssert.Contains(result.ModelText, "tetra.stl", "reports the model name");
        StringAssert.Contains(result.ModelText, "Triangles: 4", "reports the real tetrahedron facet count");
    }

    private static IClientTool Tool(Model3DViewModel vm, string name) =>
        vm.GetClientTools().Single(t => t.Name == name);

    // ── Context preview ───────────────────────────────────────────────────────────

    /// <summary>A viewport that answers each capture with a picture of the given size, counting the captures.</summary>
    private static List<int> Viewport(Model3DViewModel vm, int width, int height)
    {
        var captures = new List<int>();
        vm.CaptureViewportPngAsync = _ =>
        {
            captures.Add(captures.Count);
            var bitmap  = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, new byte[width * height * 4], width * 4);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            return Task.FromResult<byte[]?>(stream.ToArray());
        };
        return captures;
    }

    private static async Task<Model3DViewModel> TetraAsync()
    {
        var vm = new Model3DViewModel(Path.Combine(TestSampleData.Path("model3d"), "tetra.stl"), new ModelLoaderRegistry());
        await vm.LoadAsync();
        Assert.IsFalse(vm.HasError, vm.ErrorMessage);
        return vm;
    }

    [TestMethod]
    [TestCategory("Unit")]
    [CoversNode("model3d-ai-preview")]
    public async Task TheFramedModel_IsPictured_SmallEnoughForThePanel()
    {
        var vm = await TetraAsync();
        Viewport(vm, 1200, 800);

        await vm.TakeSnapshotAsync(default);

        var snapshot = (BitmapSource)vm.Snapshot!;
        Assert.AreEqual(PageSnapshot.MaxEdge, snapshot.PixelWidth, "the full-size capture is not kept");
        Assert.AreEqual(320, snapshot.PixelHeight, "its shape is");
        Assert.IsTrue(snapshot.IsFrozen, "frozen, so the preview can bind it from wherever it is built");
    }

    [TestMethod]
    [TestCategory("Unit")]
    [CoversNode("model3d-ai-preview")]
    public async Task WithNothingLoaded_NothingIsPictured()
    {
        var vm       = Make();
        var captures = Viewport(vm, 64, 48);

        await vm.TakeSnapshotAsync(default);

        Assert.AreEqual(0, captures.Count, "an empty viewport reminds no one of anything");
        Assert.IsNull(vm.Snapshot);
    }

    [TestMethod]
    [TestCategory("Unit")]
    [CoversNode("model3d-ai-preview")]
    public async Task ThePreview_RemindsOfTheModel_AndIsFreshEachTime()
    {
        var vm = await TetraAsync();

        UiThread.Run(() =>
        {
            var first  = vm.CreateContextPreview();
            var second = vm.CreateContextPreview();

            Assert.IsInstanceOfType(first, typeof(Model3DContextPreview));
            Assert.AreSame(vm, first.DataContext, "the preview binds to the page's own snapshot");
            Assert.AreNotSame(first, second, "a fresh control per selection; the last one was discarded");
            var shown = ContextPreviewProbe.VisibleTexts(first).ToList();
            CollectionAssert.Contains(shown, vm.FormatName, "the model's format");
            CollectionAssert.DoesNotContain(shown, "tetra.stl", "the panel's header names the file");
        });
    }
}
