using System.Runtime.Intrinsics.X86;
using Nexaflow.Media.Jpeg.Internal;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Components.Media;

/// <summary>
/// The inverse DCT's two forms — libjpeg-turbo's AVX2 code, ported instruction for instruction, and the scalar code
/// that does the same arithmetic in the same order where AVX2 is missing — give the same pixels for any block, the
/// out-of-range coefficients of damaged data and quantisation that overflows sixteen bits included.
/// </summary>
[TestClass]
public class InverseDctTests
{
    [TestMethod]
    [DataRow(1024, 1)]
    [DataRow(255, 16)]
    [DataRow(short.MaxValue, 255)]
    [CoversNode("media-jpeg-transforms")]
    public void TheVectorTransform_GivesTheScalarTransformsPixels(int coefficientRange, int quantRange)
    {
        if (!Avx2.IsSupported) { Assert.Inconclusive("this processor has no AVX2, so only the scalar transform runs"); return; }

        var random = new Random(coefficientRange * 31 + quantRange);
        var block = new short[64];
        var table = new ushort[64];
        Span<byte> vector = stackalloc byte[8 * 11];
        Span<byte> scalar = stackalloc byte[8 * 11];

        for (var trial = 0; trial < 20_000; trial++)
        {
            // Most blocks are mostly zeros past the first few coefficients; some are dense.
            var dense = trial % 4 == 0;
            for (var i = 0; i < 64; i++)
            {
                block[i] = dense || i < 6 || random.Next(8) == 0 ? (short)random.Next(-coefficientRange, coefficientRange + 1) : (short)0;
                table[i] = (ushort)random.Next(1, quantRange + 1);
            }
            var multipliers = InverseDct.Multipliers(table);

            vector.Clear();
            scalar.Clear();
            InverseDct.Transform(block, multipliers, vector, 11);
            InverseDct.TransformScalar(block, multipliers, scalar, 11);

            if (!vector.SequenceEqual(scalar))
                Assert.Fail($"trial {trial}: the transforms differ for coefficients [{string.Join(", ", block)}] and quantisation [{string.Join(", ", table)}]");
        }
    }
}
