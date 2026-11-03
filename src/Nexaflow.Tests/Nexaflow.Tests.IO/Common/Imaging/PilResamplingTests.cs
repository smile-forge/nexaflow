using System.Text.Json.Nodes;
using Nexaflow.IO.Common.Imaging;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Common.Imaging;

/// <summary>
/// PIL's resizes under its bicubic, Lanczos and box filters — the ones HiDream-O1 sizes the pictures it edits from with —
/// held to what PIL itself made of the same small pictures (pil-resampling-reference.json, from
/// tools/ai-models/hidream-o1/edit_reference.py). PIL works in fixed point, so each is matched exactly.
/// </summary>
[TestClass]
[CoversNode("owned-images")]
public class PilResamplingTests
{
    private static readonly JsonArray Reference = Load();

    private static JsonArray Load()
    {
        using var stream = typeof(PilResamplingTests).Assembly.GetManifestResourceStream("pil-resampling-reference.json")!;
        return JsonNode.Parse(stream)!.AsArray();
    }

    private static int Int(JsonNode node, string name) => node[name]!.GetValue<int>();

    private static byte[] Bytes(JsonNode node) => [.. node.AsArray().Select(v => (byte)v!.GetValue<int>())];

    [TestMethod]
    [DataRow(0, DisplayName = "bicubic, 37x23 to 16x11: more than halved, the filter widened")]
    [DataRow(1, DisplayName = "bicubic, 9x7 to 20x15: enlarged, overshooting clipped")]
    [DataRow(2, DisplayName = "bicubic, 30x5 to 12x14: narrowed and made taller")]
    [DataRow(3, DisplayName = "bicubic, 64x48 to 64x21: down only")]
    [DataRow(4, DisplayName = "Lanczos, 37x23 to 16x11")]
    [DataRow(5, DisplayName = "Lanczos, 9x7 to 20x15")]
    [DataRow(6, DisplayName = "Lanczos, 30x5 to 12x14")]
    [DataRow(7, DisplayName = "Lanczos, 64x48 to 21x48: across only")]
    [DataRow(8, DisplayName = "box, 37x23 to 18x11: not quite halved")]
    [DataRow(9, DisplayName = "box, 64x48 to 32x24: halved")]
    [DataRow(10, DisplayName = "box, 9x7 to 20x15: enlarged")]
    public void APilResize_IsPils_ToTheValue(int which)
    {
        var c = Reference[which]!;
        var into = c["into"]!;
        var filter = c["filter"]!.GetValue<string>() switch
        {
            "bicubic" => PilFilter.Bicubic,
            "lanczos" => PilFilter.Lanczos,
            "box"     => PilFilter.Box,
            var other => throw new InvalidDataException($"no filter {other}"),
        };

        var resized = PictureResampling.ResizeAsPil(Bytes(c["pixels"]!), Int(c, "width"), Int(c, "height"), 3, Int(into, "width"), Int(into, "height"), filter);

        CollectionAssert.AreEqual(Bytes(into["pixels"]!), resized);
    }
}
