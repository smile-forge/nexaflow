using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
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
        Assert.IsInstanceOfType(registry.LoaderFor("model.fbx"), typeof(AssimpModelLoader));
        Assert.IsInstanceOfType(registry.LoaderFor("part.3mf"), typeof(AssimpModelLoader));
        Assert.IsInstanceOfType(registry.LoaderFor("part.amf"), typeof(AssimpModelLoader));
        Assert.IsInstanceOfType(registry.LoaderFor("scene.dae"), typeof(AssimpModelLoader));
        Assert.IsNull(registry.LoaderFor("model.unknownformat"));
    }

    [TestMethod]
    public void AssimpLoader_RoundTripsFbxAnd3mf_WithGeometry()
    {
        // Round-trip the OBJ fixture through Assimp to each format, then load it back via the Assimp loader.
        foreach (var format in new[] { "fbx", "3mf" })
        {
            var path = Path.Combine(Path.GetTempPath(), $"nexaflow_test_{System.Guid.NewGuid():N}.{format}");
            using (var context = new AssimpContext())
            {
                var scene = context.ImportFile(Sample("tetra.obj"), PostProcessSteps.Triangulate);
                Assert.IsTrue(context.ExportFile(scene, path, format), $"Assimp exported {format} to round-trip.");
            }

            try
            {
                var loaded = new AssimpModelLoader().Load(path, CategoricalPalette.Fallback);
                Assert.AreEqual(format.ToUpperInvariant(), loaded.FormatName);
                Assert.IsTrue(loaded.TriangleCount > 0, $"{format} mesh has triangles");
                Assert.IsTrue(loaded.VertexCount > 0, $"{format} mesh has vertices");
                Assert.IsTrue(loaded.Geometry.Children.Count > 0, $"{format} produced renderable geometry");
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
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
    public void Tinting_SeparatesMaterialsThatShareAColour()
    {
        var palette = CategoricalPalette.Fallback;
        var grey = Color.FromRgb(0x80, 0x80, 0x80);

        // Two materials the file left the same grey (no textures) → both tinted, and to different colours.
        var tinted = CategoricalTinting.Resolve(new (Color?, bool)[] { (grey, false), (grey, false) }, palette);
        Assert.IsNotNull(tinted[0]);
        Assert.IsNotNull(tinted[1]);
        Assert.AreNotEqual(tinted[0], tinted[1], "indistinct materials are tinted distinctly");

        // Materials that already carry distinct colours are left untouched.
        var red = Color.FromRgb(0xC0, 0x10, 0x10);
        var blue = Color.FromRgb(0x10, 0x10, 0xC0);
        var kept = CategoricalTinting.Resolve(new (Color?, bool)[] { (red, false), (blue, false) }, palette);
        Assert.AreEqual(red, kept[0]);
        Assert.AreEqual(blue, kept[1]);
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
