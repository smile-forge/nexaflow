using System.Text.Json.Nodes;
using Nexaflow.IO.Common.Imaging;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Common.Imaging;

/// <summary>
/// The resizes, warps, blurs and fits a model's Python pipeline did with OpenCV, as Nexaflow does them: held to what
/// OpenCV itself made of the same small pictures (resampling-reference.json, from
/// tools/ai-models/real-esrgan-gfpgan/resampling_reference.py) — to a level of 8-bit rounding, where OpenCV works in fixed
/// point.
/// </summary>
[TestClass]
[CoversNode("picture-tools")]
public class PictureResamplingTests
{
    private static readonly JsonNode Reference = Load();

    private static JsonNode Load()
    {
        using var stream = typeof(PictureResamplingTests).Assembly.GetManifestResourceStream("resampling-reference.json")!;
        return JsonNode.Parse(stream)!;
    }

    private static byte[] Bytes(JsonNode node) => [.. node.AsArray().Select(v => (byte)v!.GetValue<int>())];

    private static float[] Floats(JsonNode node) => [.. node.AsArray().Select(v => v!.GetValue<float>())];

    private static void Within(byte[] expected, byte[] actual, int tolerance, string what)
    {
        Assert.AreEqual(expected.Length, actual.Length, what);
        int worst = expected.Zip(actual, (e, a) => Math.Abs(e - a)).Max();
        Assert.IsTrue(worst <= tolerance, $"{what}: a value is {worst} from OpenCV's");
    }

    [TestMethod]
    [DataRow("down")]
    [DataRow("up")]
    public void ALanczosResize_IsOpenCvs(string which)
    {
        var l = Reference["lanczos"]!;
        var into = l[which]!;
        var resized = PictureResampling.ResizeLanczos(Bytes(l["pixels"]!), l["width"]!.GetValue<int>(), l["height"]!.GetValue<int>(), 3,
                                                      into["width"]!.GetValue<int>(), into["height"]!.GetValue<int>());
        Within(Bytes(into["pixels"]!), resized, 1, $"resized {which}");
    }

    [TestMethod]
    public void AnAffineWarp_IsOpenCvs_PastTheEdgeTheBorder()
    {
        var w = Reference["warp"]!;
        var m = w["map"]!.AsArray().Select(v => v!.GetValue<double>()).ToArray();
        var map = new Affine2D(m[0], m[1], m[2], m[3], m[4], m[5]);
        int width = w["into"]![0]!.GetValue<int>(), height = w["into"]![1]!.GetValue<int>();

        var warped = PictureResampling.WarpAffine(Bytes(w["pixels"]!), w["width"]!.GetValue<int>(), w["height"]!.GetValue<int>(), 3, map,
                                                  (0, 0, width, height), Bytes(w["border"]!));
        Within(Bytes(w["warped"]!), warped, 1, "the warped picture");

        var plane = PictureResampling.WarpAffine(Floats(w["plane"]!), w["width"]!.GetValue<int>(), w["height"]!.GetValue<int>(), map, (0, 0, width, height));
        var expected = Floats(w["warped_plane"]!);
        Assert.IsTrue(expected.Zip(plane, (e, a) => Math.Abs(e - a)).Max() < 1e-4, "the warped plane");
    }

    [TestMethod]
    public void ARegionOfAWarp_IsThatPartOfTheWholeWarp()
    {
        var w = Reference["warp"]!;
        var m = w["map"]!.AsArray().Select(v => v!.GetValue<double>()).ToArray();
        var map = new Affine2D(m[0], m[1], m[2], m[3], m[4], m[5]);
        var pixels = Bytes(w["pixels"]!);
        var whole = PictureResampling.WarpAffine(pixels, 16, 12, 3, map, (0, 0, 14, 11), Bytes(w["border"]!));

        var part = PictureResampling.WarpAffine(pixels, 16, 12, 3, map, (3, 2, 6, 5), Bytes(w["border"]!));

        for (int y = 0; y < 5; y++)
            CollectionAssert.AreEqual(whole.AsSpan(((2 + y) * 14 + 3) * 3, 18).ToArray(), part.AsSpan(y * 18, 18).ToArray(), $"row {y}");
    }

    [TestMethod]
    public void AGaussianBlur_IsOpenCvs_TheEdgeMirrored()
    {
        var b = Reference["blur"]!;
        var plane = Floats(b["plane"]!);

        PictureResampling.GaussianBlur(plane, b["width"]!.GetValue<int>(), b["height"]!.GetValue<int>(), b["size"]!.GetValue<int>(), b["sigma"]!.GetValue<double>());

        var expected = Floats(b["blurred"]!);
        Assert.IsTrue(expected.Zip(plane, (e, a) => Math.Abs(e - a)).Max() < 1e-2, "the blurred plane, of values to 255");
    }

    [TestMethod]
    public void ASimilarityFit_IsOpenCvsLeastMedianOfSquares_APointFarOffSetAside()
    {
        var s = Reference["similarity"]!;
        var to = s["to"]!.AsArray().Select(p => (p![0]!.GetValue<double>(), p[1]!.GetValue<double>())).ToList();
        foreach (var fit in s["fits"]!.AsArray())
        {
            var from = fit!["from"]!.AsArray().Select(p => (p![0]!.GetValue<double>(), p[1]!.GetValue<double>())).ToList();
            var e = fit["affine"]!.AsArray().Select(v => v!.GetValue<double>()).ToArray();

            var m = Affine2D.Similarity(from, to);

            double[] actual = [m.A, m.B, m.C, m.D, m.E, m.F];
            for (int i = 0; i < 6; i++)
                Assert.AreEqual(e[i], actual[i], i % 3 == 2 ? 1e-2 : 1e-5, $"entry {i} of the fit from {from[0]}");
        }
    }

    [TestMethod]
    public void AMapAndItsInverse_UndoEachOther()
    {
        var m = new Affine2D(0.83, -0.21, 3.4, 0.19, 0.91, -1.7);

        var (x, y) = m.Inverse().Map(m.Map(5, 7).X, m.Map(5, 7).Y);

        Assert.AreEqual(5, x, 1e-9);
        Assert.AreEqual(7, y, 1e-9);
        Assert.AreEqual(m.Map(5, 7).X * 2, m.Scaled(2).Map(5, 7).X, 1e-9, "scaled, it lands twice as far");
    }
}
