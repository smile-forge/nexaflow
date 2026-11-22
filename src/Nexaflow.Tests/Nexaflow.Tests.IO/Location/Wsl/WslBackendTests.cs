using Nexaflow.IO.Common.Locations;
using Nexaflow.IO.Location.Wsl;
using Nexaflow.IO.Location.Wsl.Protocol;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Location.Wsl;

/// <summary>
/// The WSL backend's own rules, without a distro: how a Linux name reads under <c>\\wsl.localhost</c>, which distros
/// the registry lists, which helper a distro needs, how the wire carries a frame, and how an entry the helper read
/// becomes a location's.
/// </summary>
[TestClass]
[CoversNode("io-locations-wsl")]
public sealed class WslBackendTests
{
    // ── Names under the share ───────────────────────────────────────────────

    [TestMethod]
    public void ANameWindowsCannotHold_StandsAsThePrivateUseCharacterTheShareShows()
    {
        Assert.AreEqual("a\uF03Ab", WslNames.ToNative("a:b"));
        Assert.AreEqual("plain.txt", WslNames.ToNative("plain.txt"), "a name Windows can hold is its own");
    }

    [TestMethod]
    public void EveryCharacterThatStands_ComesBackAsItself()
    {
        const string awkward = "\\:*?\"<>|\u0001\u001F";
        Assert.AreEqual(awkward, WslNames.FromNative(WslNames.ToNative(awkward)));
    }

    [TestMethod]
    public void APrivateUseCharacterThatStandsForNothing_IsLeftAlone()
        => Assert.AreEqual("\uF0FF\uF041", WslNames.FromNative("\uF0FF\uF041"));

    [TestMethod]
    public void ADistroPath_IsAPathUnderItsShare()
    {
        Assert.AreEqual("\\\\wsl.localhost\\Ubuntu\\home\\a\uF03Ab", WslNames.NativePath("Ubuntu", "/home/a:b"));
        Assert.AreEqual(@"\\wsl.localhost\Ubuntu", WslNames.NativePath("Ubuntu", "/"));
    }

    // ── Distros ─────────────────────────────────────────────────────────────

    private static (string, Func<string, object?>) Key(string id, params (string Name, object Value)[] values)
        => (id, name => values.FirstOrDefault(v => v.Name == name).Value);

    [TestMethod]
    public void TheRegistryListsInstalledDistros_TheDefaultFirst()
    {
        var found = WslRegistry.Parse("{b}",
        [
            Key("{a}", ("DistributionName", "Debian"), ("Version", 1), ("State", 1)),
            Key("{b}", ("DistributionName", "Ubuntu"), ("Version", 2), ("State", 1)),
            Key("{c}", ("DistributionName", "Arch"), ("State", 3)),
            Key("{d}", ("Version", 2)),
        ]);

        CollectionAssert.AreEqual(new[] { "Ubuntu", "Debian" }, found.Select(d => d.Name).ToArray(),
                                  "the default first; one being installed or removed, and one with no name, are not listed");
        Assert.AreEqual(1, found.Single(d => d.Name == "Debian").Version);
        Assert.IsTrue(found[0].IsDefault);
    }

    [TestMethod]
    public void ADistroIsAConnectionNamedForIt_ReachableAtItsShare()
    {
        var connection = WslLocationProvider.ConnectionFor(new WslDistro("Ubuntu-24.04", 2, true));
        Assert.AreEqual("wsl-ubuntu-24.04", connection.Id);
        Assert.AreEqual("Ubuntu-24.04", connection.Host);
        Assert.AreEqual("wsl://Ubuntu-24.04/home", LocationPath.ToDisplayForm(connection, "/home"));

        CollectionAssert.AreEqual(new[] { @"\\wsl.localhost\Ubuntu-24.04", @"\\wsl$\Ubuntu-24.04" },
                                  new WslLocationProvider().GetNativeRoots(connection).ToArray());
    }

    [TestMethod]
    [DataRow("x86_64", false, "linux-x64")]
    [DataRow("aarch64", false, "linux-arm64")]
    [DataRow("x86_64", true, "linux-musl-x64")]
    [DataRow("aarch64", true, "linux-musl-arm64")]
    [DataRow("riscv64", false, null)]
    public void ADistroGetsTheHelperForItsMachineAndCLibrary(string machine, bool musl, string? runtime)
        => Assert.AreEqual(runtime, WslHelperDeployment.RuntimeFor(machine, musl));

    // ── The shell's command line ────────────────────────────────────────────

    [TestMethod]
    [DataRow("plain", "plain")]
    [DataRow("/home/a b", "\"/home/a b\"")]
    [DataRow("say \"hi\"", "\"say \\\"hi\\\"\"")]
    [DataRow(@"C:\a b\", "\"C:\\a b\\\\\"")]
    [DataRow("", "\"\"")]
    public void AnArgumentReachesWslAsWritten(string argument, string quoted)
        => Assert.AreEqual(quoted, WslShell.Quote(argument));

    // ── The wire ────────────────────────────────────────────────────────────

    private static WireEntry Sample => new("a b.txt", WireKind.Symlink, 12, new DateTime(2026, 11, 10, 8, 30, 0, DateTimeKind.Utc).Ticks)
    {
        CreatedTicks    = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc).Ticks,
        Mode            = 0b111_101_101,
        HasOwner        = true,
        Uid             = 1000,
        Gid             = 1000,
        Owner           = "adam",
        Group           = null,
        LinkTarget      = "../real",
        LinkToDirectory = true,
    };

    [TestMethod]
    public async Task AFrameReadsBackAsItWasWritten()
    {
        var frame = new WslWriter(WslReply.Entries, 42).Int32(1).Entry(Sample).ToFrame();
        using var stream = new MemoryStream([.. frame, .. new WslWriter(WslReply.Done, 42).ToFrame()]);

        var read = await WslWire.ReadAsync(stream, CancellationToken.None);
        Assert.IsNotNull(read);
        Assert.AreEqual((byte)WslReply.Entries, read.Value.Code);
        Assert.AreEqual(42, read.Value.Id);
        var reader = read.Value.Read();
        Assert.AreEqual(1, reader.Int32());
        Assert.AreEqual(Sample, reader.Entry());

        Assert.AreEqual((byte)WslReply.Done, (await WslWire.ReadAsync(stream, CancellationToken.None))!.Value.Code);
        Assert.IsNull(await WslWire.ReadAsync(stream, CancellationToken.None), "the stream ending between frames is the end");
    }

    [TestMethod]
    public async Task AStreamEndingInsideAFrame_IsNotAFrame()
    {
        var frame = new WslWriter(WslOp.Stat, 7).String("/etc/hosts").ToFrame();
        using var stream = new MemoryStream(frame[..^3]);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(async () => await WslWire.ReadAsync(stream, CancellationToken.None));
    }

    [TestMethod]
    public async Task ALengthNoFrameHas_IsRefused()
    {
        using var stream = new MemoryStream([0xFF, 0xFF, 0xFF, 0x7F, 1, 0, 0, 0, 0]);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(async () => await WslWire.ReadAsync(stream, CancellationToken.None));
    }

    [TestMethod]
    public void APayloadShorterThanItSays_IsRefused()
    {
        var reader = new WslReader(new byte[] { 10, 0, 0, 0, (byte)'a' });
        Assert.ThrowsExactly<InvalidDataException>(() => reader.String());
    }

    [TestMethod]
    public void AFailureFrame_IsTheLocationErrorItNames()
    {
        static Exception Failed(WslFailure failure)
        {
            var bytes = new WslWriter(WslReply.Failed, 1).Failure(failure, "no").ToFrame();
            return WslHelperLink.Failure(new WslFrame(bytes[4], 1, bytes.AsMemory(9)), "/x");
        }

        Assert.AreEqual(LocationError.NotFound, ((LocationException)Failed(WslFailure.NotFound)).Error);
        Assert.AreEqual(LocationError.AccessDenied, ((LocationException)Failed(WslFailure.AccessDenied)).Error);
        Assert.AreEqual(LocationError.Conflict, ((LocationException)Failed(WslFailure.NotEmpty)).Error);
        Assert.IsNotInstanceOfType<LocationException>(Failed(WslFailure.Other), "a failure with no common meaning stays an IOException");
    }

    // ── Entries ─────────────────────────────────────────────────────────────

    [TestMethod]
    public void ALinkToAFolder_OpensAsAFolder_AndSaysWhereItPoints()
    {
        var entry = WslSession.ToEntry(Sample);

        Assert.IsTrue(entry.IsDirectory);
        Assert.AreEqual("../real", entry.SymlinkTarget);
        Assert.AreEqual((UnixFileMode)0b111_101_101, entry.Mode);
        Assert.AreEqual("adam", entry.Owner);
        Assert.AreEqual("1000", entry.Group, "a gid that names no group is shown as its number");
        Assert.AreEqual(new DateTime(2026, 11, 10, 8, 30, 0, DateTimeKind.Utc), entry.Modified.ToUniversalTime());
    }

    [TestMethod]
    public void ADotNameIsHidden_AndAFileNobodyMayWriteIsReadOnly()
    {
        var entry = WslSession.ToEntry(new WireEntry(".profile", WireKind.File, 3, DateTime.UtcNow.Ticks) { Mode = 0b100_100_100 });

        Assert.IsTrue(entry.IsHidden);
        Assert.IsTrue(entry.IsReadOnly);
        Assert.IsNull(entry.SymlinkTarget);
        Assert.IsNull(entry.Owner, "ownership not read is not made up");
    }

    [TestMethod]
    public void APathsParentAndName()
    {
        Assert.AreEqual("/home", WslSession.ParentOf("/home/adam"));
        Assert.AreEqual("/", WslSession.ParentOf("/home"));
        Assert.IsNull(WslSession.ParentOf("/"));
        Assert.AreEqual("adam", WslSession.NameOf("/home/adam/"));
    }

    [TestMethod]
    public void OnlyWhatFollowsTheMark_IsAProgramOfTheDistros()
    {
        var listed = WslShell.ParseCommands("Welcome to the machine\n\n--nexaflow-commands--\napt\nsystemctl\n\napt\n");

        CollectionAssert.AreEquivalent(new[] { "apt", "systemctl" }, listed.ToArray(), "a profile's greeting names no program");
        Assert.AreEqual(0, WslShell.ParseCommands("no mark at all\n").Count);
    }
}
