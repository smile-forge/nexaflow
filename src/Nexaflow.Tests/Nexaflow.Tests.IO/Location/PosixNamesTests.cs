using System.Text;
using Nexaflow.IO.Common.Locations;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Location;

/// <summary>
/// A POSIX name is bytes, which need not be UTF-8. Held as text it keeps every byte — a stray one as a lone surrogate —
/// and gives each back; a person reads a stray byte as <c>\xHH</c>, an address reads back exactly, and a shell is handed
/// it as an all-ASCII <c>printf %b</c> escape.
/// </summary>
[TestClass]
[CoversNode("io-locations-wsl-names")]
public sealed class PosixNamesTests
{
    private static readonly byte[] Cafe = [0x63, 0x61, 0x66, 0xE9, 0x2E, 0x74, 0x78, 0x74];   // "caf" 0xE9 ".txt"

    [TestMethod]
    public void UTF8_IsItsOwnText()
    {
        var bytes = Encoding.UTF8.GetBytes("oké 😀.txt");

        Assert.AreEqual("oké 😀.txt", PosixNames.FromBytes(bytes));
        CollectionAssert.AreEqual(bytes, PosixNames.GetBytes("oké 😀.txt"));
        Assert.IsFalse(PosixNames.HoldsStrayBytes("oké 😀.txt"), "a surrogate pair is a character, not a stray byte");
    }

    [TestMethod]
    public void AStrayByte_IsHeldAsALoneSurrogate_AndGivenBackAsItself()
    {
        var name = PosixNames.FromBytes(Cafe);

        Assert.AreEqual("caf\uDCE9.txt", name);
        Assert.IsTrue(PosixNames.HoldsStrayBytes(name));
        CollectionAssert.AreEqual(Cafe, PosixNames.GetBytes(name));
    }

    [TestMethod]
    public void AnyBytesAtAll_ComeBackAsThemselves()
    {
        var random = new Random(20261123);
        for (int round = 0; round < 2000; round++)
        {
            var bytes = new byte[random.Next(1, 24)];
            random.NextBytes(bytes);
            for (int i = 0; i < bytes.Length; i++) if (bytes[i] == 0) bytes[i] = 1;   // no name holds a NUL

            CollectionAssert.AreEqual(bytes, PosixNames.GetBytes(PosixNames.FromBytes(bytes)), Convert.ToHexString(bytes));
        }
    }

    [TestMethod]
    public void APersonReadsAStrayByteAsItsHex()
        => Assert.AreEqual(@"caf\xE9.txt", PosixNames.Shown(PosixNames.FromBytes(Cafe)));

    [TestMethod]
    public void AnAddress_ReadsBackExactly_ABackslashInANameIncluded()
    {
        var path = "/tmp/" + PosixNames.FromBytes(Cafe) + "/back\\slash";

        var addressed = PosixNames.Addressed(path);

        Assert.AreEqual(@"/tmp/caf\xE9.txt/back\\slash", addressed);
        Assert.AreEqual(path, PosixNames.FromAddressed(addressed));
        Assert.AreEqual(@"/a\qb", PosixNames.FromAddressed(@"/a\qb"), "a backslash that starts no escape is a backslash");
    }

    [TestMethod]
    public void AShellIsHandedTheNameAsAnAsciiPrintfEscape()
    {
        Assert.AreEqual(@"caf\0351.txt", PosixNames.ForPrintf(PosixNames.FromBytes(Cafe)));
        Assert.AreEqual(@"it's a\\b\00122", PosixNames.ForPrintf("it's a\\b\n2"), "a newline, then the 2 after it");
    }
}
