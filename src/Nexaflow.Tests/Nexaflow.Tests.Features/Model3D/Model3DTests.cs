using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Nexaflow.Features.Model3D.Loaders;
using Nexaflow.Features.Model3D.ViewModels;
using Nexaflow.Tests.Fixtures;
using SharpAssimp;

namespace Nexaflow.Tests.Features.Model3D;

/// <summary>
/// The Model3D feature loads mesh files (STL/OBJ via HelixToolkit) and glTF (via SharpGLTF) into WPF
/// geometry through the loader registry, reporting triangle/vertex/material stats, and surfaces those plus
/// view-manipulation tools to the AI.
/// </summary>
[TestClass]
public class Model3DTests
{
    private static string Sample(string name) => Path.Combine(TestSampleData.Path("model3d"), name);

    [TestMethod]
    public void Registry_RoutesEachExtensionToALoader()
    {
        var registry = new ModelLoaderRegistry();

        Assert.IsInstanceOfType(registry.LoaderFor(Sample("tetra.stl")), typeof(HelixModelLoader));
        Assert.IsInstanceOfType(registry.LoaderFor(Sample("triangle.gltf")), typeof(GltfModelLoader));
        Assert.IsInstanceOfType(registry.LoaderFor("model.fbx"), typeof(FbxModelLoader));
        Assert.IsNull(registry.LoaderFor("model.unknownformat"));
    }

    [TestMethod]
    public void FbxLoader_ReadsAnFbx_WithGeometry()
    {
        // Round-trip the OBJ fixture through Assimp to a temp FBX, then load it back through our FBX loader.
        var fbxPath = Path.Combine(Path.GetTempPath(), $"nexaflow_test_{System.Guid.NewGuid():N}.fbx");
        using (var context = new AssimpContext())
        {
            var scene = context.ImportFile(Sample("tetra.obj"), PostProcessSteps.Triangulate);
            Assert.IsTrue(context.ExportFile(scene, fbxPath, "fbx"), "Assimp exported an FBX to round-trip.");
        }

        try
        {
            var loaded = new FbxModelLoader().Load(fbxPath, CategoricalPalette.Fallback);
            Assert.AreEqual("FBX", loaded.FormatName);
            Assert.IsTrue(loaded.TriangleCount > 0, "FBX mesh has triangles");
            Assert.IsTrue(loaded.VertexCount > 0, "FBX mesh has vertices");
            Assert.IsTrue(loaded.Geometry.Children.Count > 0, "FBX produced renderable geometry");
        }
        finally
        {
            if (File.Exists(fbxPath)) File.Delete(fbxPath);
        }
    }

    [TestMethod]
    public void HelixLoader_ReadsStlAndObj_WithGeometry()
    {
        var registry = new ModelLoaderRegistry();
        var palette = CategoricalPalette.Fallback;

        var stl = registry.LoaderFor(Sample("tetra.stl"))!.Load(Sample("tetra.stl"), palette);
        Assert.AreEqual("STL", stl.FormatName);
        Assert.AreEqual(4, stl.TriangleCount, "tetrahedron has four facets");
        Assert.IsTrue(stl.VertexCount > 0);

        var obj = registry.LoaderFor(Sample("tetra.obj"))!.Load(Sample("tetra.obj"), palette);
        Assert.AreEqual("OBJ", obj.FormatName);
        Assert.IsTrue(obj.TriangleCount > 0);
        Assert.IsTrue(obj.VertexCount > 0);
    }

    [TestMethod]
    public void GltfLoader_ReadsEmbeddedTriangle_AndItsMaterial()
    {
        var loaded = new GltfModelLoader().Load(Sample("triangle.gltf"), CategoricalPalette.Fallback);

        Assert.AreEqual("glTF", loaded.FormatName);
        Assert.AreEqual(1, loaded.TriangleCount);
        Assert.AreEqual(3, loaded.VertexCount);

        Assert.AreEqual(1, loaded.Materials.Count);
        Assert.AreEqual("Red", loaded.Materials[0].Name);
        Assert.IsNotNull(loaded.Materials[0].DiffuseColor, "base colour parsed from the glTF material");
    }

    [TestMethod]
    public async Task ViewModel_LoadsModel_ExposesContextAndTools()
    {
        var vm = new Model3DViewModel(Sample("tetra.stl"), new ModelLoaderRegistry());
        Assert.IsFalse(vm.IsContextReady, "context is gated until the model has loaded");

        await vm.LoadAsync();

        Assert.IsTrue(vm.IsLoaded);
        Assert.IsTrue(vm.IsContextReady);
        Assert.IsFalse(vm.HasError, vm.ErrorMessage);
        Assert.AreEqual(4, vm.TriangleCount);
        StringAssert.Contains(vm.GetContext(), "triangles");

        var toolNames = vm.GetClientTools().Select(t => t.Name).ToList();
        CollectionAssert.IsSubsetOf(
            new[] { "capture_viewport_image", "orbit_model", "roll_model", "zoom_model", "pan_model", "reset_view", "set_render_mode", "get_model_info" },
            toolNames);
    }
}
