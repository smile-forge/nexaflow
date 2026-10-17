using System.Text.Json.Nodes;
using Nexaflow.IO.Common.Imaging;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Common.Imaging;

/// <summary>
/// The two bilinear resizes SAM 3.1's paths go through, as Nexaflow does them: PIL's, which squashes a picture to the
/// encoder's square, and PyTorch's, which brings a mask to the picture — held to what PIL and PyTorch themselves made of
/// the same small pictures (sam-resampling-reference.json, from tools/ai-models/sam3.1/resampling_reference.py). PIL works
/// in fixed point and is matched exactly; PyTorch in single precision, matched to its rounding.
/// </summary>
[TestClass]
[CoversNode("picture-selection")]
public class BilinearResamplingTests
{
    private static readonly JsonNode Reference = Load();

    private static JsonNode Load()
    {
        using var stream = typeof(BilinearResamplingTests).Assembly.GetManifestResourceStream("sam-resampling-reference.json")!;
        return JsonNode.Parse(stream)!;
    }

    private static int Int(JsonNode node, string name) => node[name]!.GetValue<int>();

    [TestMethod]
    [DataRow(0, DisplayName = "37x23 to 16x11: more than halved, the filter widened")]
    [DataRow(1, DisplayName = "9x7 to 5x4")]
    [DataRow(2, DisplayName = "9x7 to 20x15: enlarged")]
    [DataRow(3, DisplayName = "30x5 to 12x14: narrowed and made taller")]
    [DataRow(4, DisplayName = "64x48 to 21x21")]
    [DataRow(5, DisplayName = "13x13 to 13x6: down only")]
    public void APilBilinearResize_IsPils_ToTheValue(int which)
    {
        var c = Reference["pil"]![which]!;
        var into = c["into"]!;
        byte[] source = [.. c["pixels"]!.AsArray().Select(v => (byte)v!.GetValue<int>())];
        byte[] expected = [.. into["pixels"]!.AsArray().Select(v => (byte)v!.GetValue<int>())];

        var resized = PictureResampling.ResizeBilinearAsPil(source, Int(c, "width"), Int(c, "height"), 3, Int(into, "width"), Int(into, "height"));

        CollectionAssert.AreEqual(expected, resized);
    }

    [TestMethod]
    [DataRow(0, DisplayName = "13x11 to 40x36: enlarged")]
    [DataRow(1, DisplayName = "13x11 to 5x4: shrunk, nothing averaged")]
    [DataRow(2, DisplayName = "7x9 to 7x23: taller only")]
    [DataRow(3, DisplayName = "18x18 to 63x7")]
    public void APlaneResizedBilinearly_IsPyTorchs(int which)
    {
        var c = Reference["torch"]![which]!;
        var into = c["into"]!;
        float[] source = [.. c["plane"]!.AsArray().Select(v => v!.GetValue<float>())];
        float[] expected = [.. into["plane"]!.AsArray().Select(v => v!.GetValue<float>())];

        var resized = PictureResampling.ResizePlaneBilinear(source, Int(c, "width"), Int(c, "height"), Int(into, "width"), Int(into, "height"));

        Assert.AreEqual(expected.Length, resized.Length);
        float worst = expected.Zip(resized, (e, a) => Math.Abs(e - a)).Max();
        Assert.IsTrue(worst < 1e-5f, $"a value is {worst} from PyTorch's");
    }
}
