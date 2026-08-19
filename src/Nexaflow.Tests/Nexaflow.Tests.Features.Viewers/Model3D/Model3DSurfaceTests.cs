using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Nexaflow.Features.Model3D.Loaders;
using Nexaflow.Features.Model3D.ViewModels;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Model3D;

/// <summary>
/// What the 3D tab tells the user around the geometry: the stats footer, the material inspector and its
/// toggle, and the error overlay that replaces the model when a file cannot be opened.
/// <para>
/// These readouts are the only way to tell a partial render from a complete one — a viewport that quietly
/// shows a subset of a file looks exactly like one that shows all of it — so each is asserted against a
/// real sample rather than through the live viewport.
/// </para>
/// </summary>
[TestClass]
public class Model3DSurfaceTests
{
    private static string Sample(string name) => Path.Combine(TestSampleData.Path("model3d"), name);

    private static async Task<Model3DViewModel> Load(string sample)
    {
        var vm = new Model3DViewModel(Sample(sample), new ModelLoaderRegistry());
        await vm.LoadAsync();
        return vm;
    }

    // ── Stats footer ──────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("model3d-stats-format")]
    public async Task Footer_NamesTheFormatTheLoaderActuallyUsed()
    {
        Assert.AreEqual("STL", (await Load("tetra.stl")).FormatName);
        Assert.AreEqual("glTF", (await Load("triangle.gltf")).FormatName);
    }

    [TestMethod]
    [CoversNode("model3d-stats-counts")]
    public async Task Footer_ReportsTheRealGeometryCounts_AndTheFileSize()
    {
        var vm = await Load("tetra.stl");

        Assert.AreEqual(4, vm.TriangleCount, "a tetrahedron has four facets");
        Assert.IsTrue(vm.VertexCount > 0);
        Assert.IsTrue(vm.MeshCount > 0);
        Assert.IsFalse(string.IsNullOrWhiteSpace(vm.FileSizeText));
    }

    // ── Inspector + its toggle ────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("model3d-materials")]
    public async Task Inspector_ListsEachMaterial_WithSomethingToShowForIt()
    {
        var vm = await Load("triangle.gltf");

        Assert.IsTrue(vm.HasMaterials);
        var material = vm.Materials.Single();
        Assert.AreEqual("Red", material.Name);
        Assert.IsTrue(material.HasSwatch, "a material with a colour must show its swatch");
        Assert.IsFalse(string.IsNullOrWhiteSpace(material.Detail), "and say what that colour is");
    }

    [TestMethod]
    [CoversNode("model3d-unsupported")]
    public async Task NotRenderedList_StaysEmpty_WhenTheFileHoldsNothingBesidesMesh()
    {
        var vm = await Load("tetra.stl");

        Assert.AreEqual(0, vm.UnsupportedElements.Count,
                        "a mesh-only file must not be reported as partially rendered");
        Assert.IsFalse(vm.HasUnsupported);
    }

    [TestMethod]
    [CoversNode("model3d-inspector-toggle")]
    public async Task InspectorToggle_AppearsAndOpensOnlyWhenThereIsSomethingToInspect()
    {
        var withMaterials = await Load("triangle.gltf");
        Assert.IsTrue(withMaterials.HasInspectorContent, "the toggle is hidden without content");
        Assert.IsTrue(withMaterials.ShowInspector, "and the panel opens by default when there is some");

        withMaterials.ShowInspector = false;
        Assert.IsTrue(withMaterials.HasInspectorContent,
                      "closing the panel must not retract the toggle that reopens it");
    }

    // ── Error overlay ─────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("model3d-error-overlay")]
    public async Task UnknownExtension_ShowsTheOverlay_AndStillReleasesTheContextGate()
    {
        var vm = new Model3DViewModel("model.unknownformat", new ModelLoaderRegistry());

        await vm.LoadAsync();

        Assert.IsTrue(vm.HasError);
        StringAssert.Contains(vm.ErrorMessage, ".unknownformat");
        Assert.IsTrue(vm.IsLoaded, "a failed load must still release the AI send gate, or the tab hangs");
        StringAssert.Contains(vm.GetContext(), "failed to load");
    }

    [TestMethod]
    [CoversNode("model3d-error-overlay")]
    public async Task AFileThatParsesButHoldsNoMesh_IsReportedRatherThanShownAsAnEmptyScene()
    {
        var path = Path.Combine(Path.GetTempPath(), $"nexaflow_empty_{System.Guid.NewGuid():N}.obj");
        File.WriteAllText(path, "# an OBJ with no faces at all\n");
        try
        {
            var vm = new Model3DViewModel(path, new ModelLoaderRegistry());
            await vm.LoadAsync();

            Assert.IsTrue(vm.HasError, "an empty viewport looks identical to a model that failed to draw");
            StringAssert.Contains(vm.ErrorMessage, "no renderable mesh");
        }
        finally { File.Delete(path); }
    }
}
