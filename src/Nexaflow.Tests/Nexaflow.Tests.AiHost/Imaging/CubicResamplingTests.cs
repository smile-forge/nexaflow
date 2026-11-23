using System.Text.Json.Nodes;
using Nexaflow.AiHost.Imaging;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.AiHost.Imaging;

/// <summary>
/// OpenCV's cubic resize, which Depth Anything V2's infer_image brings a picture to the size its network reads with — held to
/// what OpenCV itself made of the same small pictures (depth-cubic-reference.json, from
/// tools/ai-models/depth-anything-v2-small/resampling_reference.py). OpenCV works in double precision with single precision
/// weights, and is matched to its rounding.
/// </summary>
[TestClass]
[CoversNode("scene-depth")]
public class CubicResamplingTests
{
    private static readonly JsonNode Reference = Load();

    private static JsonNode Load()
    {
        using var stream = typeof(CubicResamplingTests).Assembly.GetManifestResourceStream("depth-cubic-reference.json")!;
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

        var resized = ReferenceResampling.ResizeCubicAsOpenCv(source, Int(c, "width"), Int(c, "height"), 3, Int(into, "width"), Int(into, "height"));

        Assert.AreEqual(expected.Length, resized.Length);
        double worst = expected.Zip(resized, (e, a) => Math.Abs(e - a)).Max();
        Assert.IsTrue(worst < 1e-7, $"a value is {worst} from OpenCV's");
    }
}
