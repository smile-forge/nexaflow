using Nexaflow.IO.Common.Locations;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Location;

/// <summary>
/// A remote name inside a Windows-shaped path, and the display form people type. The load-bearing promise is the
/// round trip: whatever a server calls a file, encoding it into a segment and decoding it back gives that name, and
/// the segment is one no Windows path rule trips over.
/// </summary>
[TestClass]
[CoversNode("io-locations-paths")]
public sealed class LocationPathTests
{
    [TestMethod]
    public void ANameWithNothingToEscapeIsItsOwnSegment()
    {
        const string name = "notes v2 (final).md";
        Assert.AreSame(name, LocationPath.EncodeSegment(name));
    }

    [TestMethod]
    [DataRow("a:b.md",   "a%3Ab.md")]
    [DataRow("what?",    "what%3F")]
    [DataRow(@"back\sl", "back%5Csl")]
    [DataRow("50%",      "50%25")]
    [DataRow("dots...",  "dots..%2E")]
    [DataRow("space ",   "space%20")]
    [DataRow("tab\there","tab%09here")]
    public void ACharacterNoWindowsSegmentCanHoldIsPercentEncoded(string name, string segment)
        => Assert.AreEqual(segment, LocationPath.EncodeSegment(name));

    [TestMethod]
    [DataRow("a:b.md")]
    [DataRow("<*|\">")]
    [DataRow("100%25 real")]
    [DataRow("trailing. ")]
    [DataRow("ümlaut ☃.txt")]
    [DataRow(".hidden")]
    public void EncodingThenDecodingGivesBackTheServersName(string name)
    {
        var segment = LocationPath.EncodeSegment(name);

        Assert.AreEqual(name, LocationPath.DecodeSegment(segment));
        Assert.AreEqual(-1, segment.IndexOfAny(['\\', '/', ':', '*', '?', '"', '<', '>', '|']));
        Assert.IsFalse(segment.EndsWith('.') || segment.EndsWith(' '));
    }

    [TestMethod]
    public void AStrayPercentDecodesToItself()
        => Assert.AreEqual("100%", LocationPath.DecodeSegment("100%"));

    [TestMethod]
    public void TheRemainderAfterTheRootBecomesAnAbsoluteProviderPath()
    {
        Assert.AreEqual("/", LocationPath.ToProviderPath(string.Empty));
        Assert.AreEqual("/home/adam/a:b.md", LocationPath.ToProviderPath(@"home\adam\a%3Ab.md"));
        Assert.AreEqual("/home/adam", LocationPath.ToProviderPath("home/adam/"));
    }

    [TestMethod]
    public void AProviderPathBecomesARemainderWithEveryNameEncoded()
    {
        Assert.AreEqual(string.Empty, LocationPath.ToRemainder("/"));
        Assert.AreEqual(@"home\adam\a%3Ab.md", LocationPath.ToRemainder("/home/adam/a:b.md"));
    }

    [TestMethod]
    public void CaseMattersOnlyWhereTheLocationSaysItDoes()
    {
        Assert.IsTrue(LocationPath.Comparer(LocationCapabilities.None).Equals("Readme", "README"));
        Assert.IsFalse(LocationPath.Comparer(LocationCapabilities.CaseSensitive).Equals("Readme", "README"));
        Assert.AreEqual(StringComparison.Ordinal, LocationPath.Comparison(LocationCapabilities.CaseSensitive));
    }

    [TestMethod]
    public void AnIdIsOneSegment()
    {
        Assert.IsTrue(LocationPath.IsSegmentSafeId("sftp-nas"));
        Assert.IsFalse(LocationPath.IsSegmentSafeId("a/b"));
        Assert.IsFalse(LocationPath.IsSegmentSafeId("c:"));
        Assert.IsFalse(LocationPath.IsSegmentSafeId("  "));
    }

    // ── Display form ────────────────────────────────────────────────────────

    private static readonly ConnectionProfile Nas = new("sftp-nas", "sftp", "NAS", "nas") { User = "adam", Port = 2222 };

    [TestMethod]
    public void TheDisplayFormNamesKindUserHostPortAndTheRawPath()
        => Assert.AreEqual("sftp://adam@nas:2222/home/adam/a:b.md", LocationPath.ToDisplayForm(Nas, "/home/adam/a:b.md"));

    [TestMethod]
    public void AConnectionWithNoUserOrPortLeavesThemOut()
        => Assert.AreEqual("wsl://Ubuntu/home",
            LocationPath.ToDisplayForm(new ConnectionProfile("wsl-ubuntu", "wsl", "Ubuntu", "Ubuntu"), "/home"));

    [TestMethod]
    public void ADisplayFormReadsBackIntoItsParts()
    {
        Assert.IsTrue(LocationPath.TryParseDisplayForm("  SFTP://adam@nas:2222/home/adam/a:b.md ", out var address));

        Assert.AreEqual(new LocationAddress("sftp", "adam", "nas", 2222, "/home/adam/a:b.md"), address);
        Assert.IsTrue(LocationPath.Matches(address, Nas));
    }

    [TestMethod]
    public void ASignInThatIsAnEmailAddressKeepsItsAt()
    {
        Assert.IsTrue(LocationPath.TryParseDisplayForm("ftp://adam@example.com@files.example.com/pub", out var address));

        Assert.AreEqual("adam@example.com", address.User);
        Assert.AreEqual("files.example.com", address.Host);
    }

    [TestMethod]
    public void AHostAloneIsTheRoot()
    {
        Assert.IsTrue(LocationPath.TryParseDisplayForm("smb://nas", out var address));
        Assert.AreEqual("/", address.Path);
    }

    [TestMethod]
    [DataRow(@"C:\Users\adam")]
    [DataRow("notes.md")]
    [DataRow("://nas/x")]
    [DataRow("sftp://adam@/x")]
    [DataRow("sftp://nas:99999/x")]
    [DataRow("sftp://nas:port/x")]
    public void TextThatIsNotADisplayFormIsLeftToOtherReadings(string text)
        => Assert.IsFalse(LocationPath.TryParseDisplayForm(text, out _));

    [TestMethod]
    public void AnAddressNamingAnotherUserOrPortIsNotThatConnection()
    {
        Assert.IsTrue(LocationPath.TryParseDisplayForm("sftp://eve@nas/x", out var otherUser));
        Assert.IsTrue(LocationPath.TryParseDisplayForm("sftp://nas:22/x", out var otherPort));
        Assert.IsTrue(LocationPath.TryParseDisplayForm("sftp://nas/x", out var unstated));

        Assert.IsFalse(LocationPath.Matches(otherUser, Nas));
        Assert.IsFalse(LocationPath.Matches(otherPort, Nas));
        Assert.IsTrue(LocationPath.Matches(unstated, Nas));
    }
}
