using Nexaflow.Media.Jpeg2000;
using Nexaflow.Media.Jpeg2000.Internal;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Components.Media;

/// <summary>
/// The MQ arithmetic decoder on its own, against the test sequence ITU-T T.88 (JBIG2) Annex H.2 publishes for the
/// same coder: thirty encoded bytes that decode, in one context from its first state, to a known 256 decisions.
/// It owes nothing to libopenjp2, and it pins the coder below the passes that drive it.
/// </summary>
[TestClass]
[CoversNode("media-jpeg2000-code-blocks")]
public class MqDecoderTests
{
    private static readonly byte[] Encoded =
    [
        0x84, 0xC7, 0x3B, 0xFC, 0xE1, 0xA1, 0x43, 0x04, 0x02, 0x20, 0x00, 0x00, 0x41, 0x0D, 0xBB, 0x86,
        0xF4, 0x31, 0x7F, 0xFF, 0x88, 0xFF, 0x37, 0x47, 0x1A, 0xDB, 0x6A, 0xDF, 0xFF, 0xAC,
    ];

    private static readonly byte[] Decisions =
    [
        0x00, 0x02, 0x00, 0x51, 0x00, 0x00, 0x00, 0xC0, 0x03, 0x52, 0x87, 0x2A, 0xAA, 0xAA, 0xAA, 0xAA,
        0x82, 0xC0, 0x20, 0x00, 0xFC, 0xD7, 0x9E, 0xF6, 0xBF, 0x7F, 0xED, 0x90, 0x4F, 0x46, 0xA3, 0xBF,
    ];

    [TestMethod]
    public void TheStandardsTestSequence_DecodesToItsDecisions()
    {
        // The decoder marks the end of its data with two bytes of its own, so it is handed room for them.
        var data = new byte[Encoded.Length + Constants.COMMON_CBLK_DATA_EXTRA];
        Encoded.CopyTo(data, 0);
        var mqc = new MQCoder();
        mqc.ResetStates();
        mqc.InitDec(data, 0, (uint)Encoded.Length, Constants.COMMON_CBLK_DATA_EXTRA);

        var decoded = new byte[Decisions.Length];
        for (var bit = 0; bit < decoded.Length * 8; bit++)
        {
            if (mqc.Decode())
                decoded[bit >> 3] |= (byte)(0x80 >> (bit & 7));
        }
        mqc.FinishDec();

        CollectionAssert.AreEqual(Decisions, decoded);
        CollectionAssert.AreEqual(Encoded, data[..Encoded.Length], "the bytes the end marker overwrote are put back");
    }
}
