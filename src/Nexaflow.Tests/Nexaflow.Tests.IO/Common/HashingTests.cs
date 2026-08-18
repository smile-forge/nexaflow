using System.IO;
using System.Threading.Tasks;
using Nexaflow.IO.Common;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Common;

[TestClass]
[CoversNode("iocommon-transforms")]
public class HashingTests
{
    [TestMethod]
    public void Md5_And_Sha256_KnownVectors()
    {
        Assert.AreEqual("900150983cd24fb0d6963f7d28e17f72", Hashing.Md5("abc"));
        Assert.AreEqual("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", Hashing.Sha256("abc"));
    }

    [TestMethod]
    public void EmptyString_KnownVectors()
    {
        Assert.AreEqual("d41d8cd98f00b204e9800998ecf8427e", Hashing.Md5(string.Empty));
        Assert.AreEqual("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", Hashing.Sha256(string.Empty));
    }

    [TestMethod]
    public async Task FileHashes_MatchStringHashes()
    {
        var path = Path.Combine(Path.GetTempPath(), $"nexa-hash-{System.Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(path, "abc"); // UTF-8, no BOM
        try
        {
            Assert.AreEqual(Hashing.Md5("abc"),    await Hashing.Md5FileAsync(path));
            Assert.AreEqual(Hashing.Sha256("abc"), await Hashing.Sha256FileAsync(path));
        }
        finally { File.Delete(path); }
    }
}
