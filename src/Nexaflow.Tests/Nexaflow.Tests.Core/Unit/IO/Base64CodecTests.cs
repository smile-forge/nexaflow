using Nexaflow.IO.Common;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit.IO;

[TestClass]
[CoversNode("iocommon-transforms")]
public class Base64CodecTests
{
    [TestMethod]
    public void Encode_KnownVector()
    {
        Assert.AreEqual("aGVsbG8=", Base64Codec.Encode("hello"));
    }

    [TestMethod]
    public void RoundTrip_Unicode()
    {
        const string s = "héllo — 世界 🌍";
        Assert.IsTrue(Base64Codec.TryDecode(Base64Codec.Encode(s), out var back));
        Assert.AreEqual(s, back);
    }

    [TestMethod]
    public void TryDecode_ToleratesSurroundingWhitespace()
    {
        Assert.IsTrue(Base64Codec.TryDecode("  aGVsbG8=  ", out var back));
        Assert.AreEqual("hello", back);
    }

    [TestMethod]
    public void TryDecode_MalformedInput_ReturnsFalse()
    {
        Assert.IsFalse(Base64Codec.TryDecode("not valid base64!!", out var back));
        Assert.AreEqual(string.Empty, back);
    }
}
