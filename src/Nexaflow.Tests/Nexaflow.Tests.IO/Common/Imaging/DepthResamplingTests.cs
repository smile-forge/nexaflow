using System.Text.Json.Nodes;
using Nexaflow.IO.Common.Imaging;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Common.Imaging;

/// <summary>
/// The two resizes Depth Anything V2's infer_image goes through, as Nexaflow does them: OpenCV's cubic, which brings the
/// picture to the size the network reads, and PyTorch's bilinear with the corners aligned, which brings the disparity back to
/// the picture — held to what OpenCV and PyTorch themselves made of the same small pictures (depth-resampling-reference.json,
/// from tools/ai-models/depth-anything-v2-small/resampling_reference.py). OpenCV works in double precision with single
/// precision weights, PyTorch in single precision, and each is matched to its rounding.
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
    [DataRow(0, DisplayName = "37x23 to 16x11: more than halved, nothing averaged")]
    [DataRow(1, DisplayName = "9x7 to 28x14: enlarged")]
    [DataRow(2, DisplayName = "30x5 to 12x14: narrowed and made taller")]
    [DataRow(3, DisplayName = "64x48 to 42x28")]
    [DataRow(4, DisplayName = "13x13 to 13x6: down only")]
    public void ACubicResize_IsOpenCvs_ToItsRounding(int which)
    {
        var c = Reference["cubic"]![which]!;
        var into = c["into"]!;
        double[] source = [.. c["pixels"]!.AsArray().Select(v => v!.GetValue<double>())];
        double[] expected = [.. into["pixels"]!.AsArray().Select(v => v!.GetValue<double>())];

        var resized = PictureResampling.ResizeCubicAsOpenCv(source, Int(c, "width"), Int(c, "height"), 3, Int(into, "width"), Int(into, "height"));

        Assert.AreEqual(expected.Length, resized.Length);
        double worst = expected.Zip(resized, (e, a) => Math.Abs(e - a)).Max();
        Assert.IsTrue(worst < 1e-7, $"a value is {worst} from OpenCV's");
    }

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
