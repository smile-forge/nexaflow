using System.Text.Json.Nodes;
using Nexaflow.IO.Common.Imaging;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Common.Imaging;

/// <summary>
/// PyTorch's bilinear resize with the corners aligned, which Depth Anything V2's infer_image brings the disparity back to the
/// picture with, as Nexaflow does it — held to what PyTorch itself made of the same small planes
/// (depth-resampling-reference.json, from tools/ai-models/depth-anything-v2-small/resampling_reference.py), in single
/// precision.
/// </summary>
[TestClass]
[CoversNode("scene-depth")]
public class DepthResamplingTests
{
    private static readonly JsonNode Reference = Load();

    private static JsonNode Load()
    {
        using var stream = typeof(DepthResamplingTests).Assembly.GetManifestResourceStream("depth-resampling-reference.json")!;
        return JsonNode.Parse(stream)!;
    }

    private static int Int(JsonNode node, string name) => node[name]!.GetValue<int>();

    [TestMethod]
    [DataRow(0, DisplayName = "13x11 to 40x36: enlarged")]
    [DataRow(1, DisplayName = "37x26 to 10x7: shrunk")]
    [DataRow(2, DisplayName = "7x9 to 7x23: taller only")]
    [DataRow(3, DisplayName = "5x1 to 12x1: one row")]
    public void APlaneResizedWithItsCornersAligned_IsPyTorchs(int which)
    {
        var c = Reference["aligned"]![which]!;
        var into = c["into"]!;
        float[] source = [.. c["plane"]!.AsArray().Select(v => v!.GetValue<float>())];
        float[] expected = [.. into["plane"]!.AsArray().Select(v => v!.GetValue<float>())];

        var resized = PictureResampling.ResizePlaneBilinearAligned(source, Int(c, "width"), Int(c, "height"), Int(into, "width"), Int(into, "height"));

        Assert.AreEqual(expected.Length, resized.Length);
        float worst = expected.Zip(resized, (e, a) => Math.Abs(e - a)).Max();
        Assert.IsTrue(worst < 1e-5f, $"a value is {worst} from PyTorch's");
    }
}
