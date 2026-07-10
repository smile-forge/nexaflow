using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.Model3D.Loaders;
using Nexaflow.Features.Model3D.ViewModels;
using Nexaflow.Tests.Fixtures;

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
    [CoversNode("model3d-inspector")]
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
    [CoversNode("model3d-ai-tools")]
    public void GetContext_GatedUntilLoaded()
    {
        var vm = Make();
        Assert.IsFalse(vm.IsContextReady, "context is gated until the model has loaded");
        StringAssert.Contains(vm.GetContext(), "still loading");
    }

    [TestMethod]
    [CoversNode("model3d-ai-tools")]
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
    [CoversNode("model3d-ai-tools")]
    public async Task CameraTools_FailGracefully_WithoutViewport()
    {
        var vm = Make(); // no view attached → camera/capture delegates are null

        foreach (var name in new[] { "orbit_model", "roll_model", "zoom_model", "pan_model", "reset_view", "capture_viewport_image" })
        {
            var result = await Tool(vm, name).InvokeAsync(new JsonObject(), default);
            Assert.IsFalse(result.Success, $"'{name}' should report a friendly error when no viewport is wired.");
        }
    }

    private static IClientTool Tool(Model3DViewModel vm, string name) =>
        vm.GetClientTools().Single(t => t.Name == name);
}
