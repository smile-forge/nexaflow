using System.Text.Json.Nodes;
using Nexaflow.IO.Common.Imaging;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Common.Imaging;

/// <summary>
/// Pictures brought to whole patches within a square, as HiDream-O1's <c>resize_pilimage</c> brings the pictures it
/// edits from — held to what it made of them (patch-fitting-reference.json, from
/// tools/ai-models/hidream-o1/edit_reference.py): the size for pictures enlarged, reduced and box-halved first, and the
/// pixels themselves, covered bicubically and cropped from the middle.
/// </summary>
[TestClass]
[CoversNode("owned-images")]
public class PatchFittingTests
{
    private static readonly JsonNode Reference = Load();

    private static JsonNode Load()
    {
        using var stream = typeof(PatchFittingTests).Assembly.GetManifestResourceStream("patch-fitting-reference.json")!;
        return JsonNode.Parse(stream)!;
    }

    private static int Int(JsonNode node, string name) => node[name]!.GetValue<int>();

    private static byte[] Bytes(JsonNode node) => [.. node.AsArray().Select(v => (byte)v!.GetValue<int>())];

    [TestMethod]
    public void APicture_IsBroughtToTheLargestSizeOfWholePatchesWithinTheSquare_AsHiDreamBringsIt()
    {
        foreach (var c in Reference["fitted"]!.AsArray())
        {
            int width = Int(c!, "width"), height = Int(c!, "height"), side = Int(c!, "side");
            var into = c!["into"]!.AsArray();

            Assert.AreEqual((into[0]!.GetValue<int>(), into[1]!.GetValue<int>()), PatchFitting.SizeOf(width, height, side, 32),
                            $"{width}x{height} within {side}²");
        }
    }

    [TestMethod]
    [DataRow("whole", DisplayName = "enlarged, covered and cropped")]
    [DataRow("halved", DisplayName = "box-halved first, then reduced")]
    public void APicturesPixels_AreHiDreamsToTheValue(string which)
    {
        var c = Reference[which]!;
        var into = c["into"]!;

        var (rgb, width, height) = PatchFitting.Fit(Bytes(c["pixels"]!), Int(c, "width"), Int(c, "height"), Int(c, "side"), 32);

        Assert.AreEqual((Int(into, "width"), Int(into, "height")), (width, height));
        CollectionAssert.AreEqual(Bytes(into["pixels"]!), rgb);
    }
}
