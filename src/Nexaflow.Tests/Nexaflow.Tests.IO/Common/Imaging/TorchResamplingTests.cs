using System.Text.Json.Nodes;
using Nexaflow.IO.Common.Imaging;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Common.Imaging;

/// <summary>
/// PyTorch's antialiased resizes of 8-bit pictures — the ones torchvision and Qwen2-VL's image processor size pictures with
/// — held to what PyTorch itself made of the same small pictures (torch-resampling-reference.json, from
/// tools/ai-models/qwen-image-edit/edit_reference.py). PyTorch works in 16-bit fixed point, so each is matched exactly.
/// </summary>
[TestClass]
[CoversNode("owned-images")]
public class TorchResamplingTests
{
    private static readonly JsonArray Reference = Load();

    private static JsonArray Load()
    {
        using var stream = typeof(TorchResamplingTests).Assembly.GetManifestResourceStream("torch-resampling-reference.json")!;
        return JsonNode.Parse(stream)!.AsArray();
    }

    private static int Int(JsonNode node, string name) => node[name]!.GetValue<int>();

    private static byte[] Bytes(JsonNode node) => [.. node.AsArray().Select(v => (byte)v!.GetValue<int>())];

    [TestMethod]
    [DataRow(0, DisplayName = "bicubic, 37x23 to 16x11: more than halved, the filter widened")]
    [DataRow(1, DisplayName = "bicubic, 9x7 to 20x15: enlarged, overshooting clipped")]
    [DataRow(2, DisplayName = "bicubic, 30x5 to 12x14: narrowed and made taller")]
    [DataRow(3, DisplayName = "bicubic, 96x72 to 98x70: widened a little and lowered, as the processor rounds to 28")]
    [DataRow(4, DisplayName = "bicubic, 64x48 to 64x21: down only")]
    [DataRow(5, DisplayName = "bilinear, 37x23 to 16x11")]
    [DataRow(6, DisplayName = "bilinear, 9x7 to 20x15")]
    [DataRow(7, DisplayName = "bilinear, 64x48 to 21x48: across only")]
    public void ATorchResize_IsTorchs_ToTheValue(int which)
    {
        var c = Reference[which]!;
        var into = c["into"]!;
        var filter = c["filter"]!.GetValue<string>() == "bicubic" ? PilFilter.Bicubic : PilFilter.Bilinear;

        var resized = PictureResampling.ResizeAsTorch(Bytes(c["pixels"]!), Int(c, "width"), Int(c, "height"), 3, Int(into, "width"), Int(into, "height"), filter);

        CollectionAssert.AreEqual(Bytes(into["pixels"]!), resized);
    }

    [TestMethod]
    public void ATorchResize_TakesOnlyTheFiltersTorchResizes8BitPicturesWith()
        => Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => PictureResampling.ResizeAsTorch(new byte[12], 2, 2, 3, 1, 1, PilFilter.Lanczos));
}
