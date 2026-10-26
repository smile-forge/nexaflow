using System.Text;
using Nexaflow.IO.Common.Locations;

namespace Nexaflow.Tests.IO.Location;

/// <summary>What a provider under test is, and how to break its link.</summary>
/// <param name="Drop">Drops every live session to the host, as a server restart does.</param>
public sealed record LocationUnderTest(
    ILocationProvider Provider, ConnectionProfile Profile, ICredentialSource Credentials, Action Drop);

/// <summary>
/// The bar every <see cref="ILocationProvider"/> clears — listing, ranged reads, atomic writes, rename, delete,
/// conflicts, cancellation and reconnect — written once and inherited per backend, so a new protocol is proven by
/// the same tests as the last. A derived class says only how to reach its host. Each test works in a fresh folder
/// of its own, so a backend's real server can be shared by the whole run.
/// </summary>
public abstract class LocationSessionContract
{
    private LocationUnderTest _under = null!;
    private ILocationSession _session = null!;
    private string _root = string.Empty;

    /// <summary>The provider, profile and credentials that reach a host this run may write to.</summary>
    protected abstract LocationUnderTest Connect();

    [TestInitialize]
    public async Task OpenSession()
    {
        _under   = Connect();
        _session = await _under.Provider.ConnectAsync(_under.Profile, _under.Credentials, CancellationToken.None);
        _root    = $"/contract-{Guid.NewGuid():N}";
        await _session.CreateDirectoryAsync(_root, CancellationToken.None);
    }

    [TestCleanup]
    public async Task CloseSession()
    {
        try { await _session.DeleteAsync(_root, recursive: true, CancellationToken.None); }
        catch (LocationException) { /* a test that dropped the link leaves its folder to the next run's host */ }
        await _session.DisposeAsync();
    }

    private string At(string name) => _root + "/" + name;

    private async Task WriteAsync(string path, byte[] bytes, WriteMode mode = WriteMode.Replace, LocationEntry? expected = null)
    {
        await using var write = await _session.BeginWriteAsync(path, mode, expected, CancellationToken.None);
        await write.Content.WriteAsync(bytes);
        await write.CommitAsync(CancellationToken.None);
    }

    private Task WriteAsync(string path, string text, WriteMode mode = WriteMode.Replace)
        => WriteAsync(path, Encoding.UTF8.GetBytes(text), mode);

    private async Task<string> ReadAsync(string path)
    {
        await using var stream = await _session.OpenReadAsync(path, ReadIntent.Whole, CancellationToken.None);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }

    private async Task<List<LocationEntry>> ListAsync(string path)
    {
        var entries = new List<LocationEntry>();
        await foreach (var entry in _session.EnumerateAsync(path, CancellationToken.None)) entries.Add(entry);
        return entries;
    }

    private static async Task<LocationError> FailureOf(Func<Task> operation)
    {
        try { await operation(); }
        catch (LocationException e) { return e.Error; }
        Assert.Fail("Expected a LocationException.");
        return default;
    }

    // ── Listing ─────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task ListingGivesEveryChildWithItsKindAndSize()
    {
        await WriteAsync(At("a.txt"), "alpha");
        await WriteAsync(At("b.txt"), "bravo!");
        await _session.CreateDirectoryAsync(At("sub"), CancellationToken.None);

        var entries = (await ListAsync(_root)).OrderBy(e => e.Name, StringComparer.Ordinal).ToList();

        CollectionAssert.AreEqual(new[] { "a.txt", "b.txt", "sub" }, entries.Select(e => e.Name).ToArray());
        CollectionAssert.AreEqual(new[] { false, false, true }, entries.Select(e => e.IsDirectory).ToArray());
        Assert.AreEqual(6, entries[1].Size);
    }

    [TestMethod]
    public async Task ListingAMissingFolderIsNotFound()
        => Assert.AreEqual(LocationError.NotFound, await FailureOf(() => ListAsync(At("absent"))));

    [TestMethod]
    public async Task AMissingPathHasNoEntry()
        => Assert.IsNull(await _session.GetEntryAsync(At("absent"), CancellationToken.None));

    [TestMethod]
    public async Task TheRootIsAFolderWithNoName()
    {
        var root = await _session.GetEntryAsync("/", CancellationToken.None);

        Assert.IsNotNull(root);
        Assert.IsTrue(root.IsDirectory);
        Assert.AreEqual(string.Empty, root.Name);
    }

    // ── Reading ─────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task ARandomReadSeeksToAnyOffset()
    {
        var bytes = Enumerable.Range(0, 64 * 1024).Select(i => (byte)(i * 7)).ToArray();
        await WriteAsync(At("blob.bin"), bytes);

        await using var stream = await _session.OpenReadAsync(At("blob.bin"), ReadIntent.Random, CancellationToken.None);
        Assert.IsTrue(stream.CanSeek);
        stream.Seek(40_000, SeekOrigin.Begin);
        var slice = new byte[16];
        await stream.ReadExactlyAsync(slice);

        CollectionAssert.AreEqual(bytes[40_000..40_016], slice);
    }

    [TestMethod]
    public async Task ReadingAMissingFileIsNotFound()
        => Assert.AreEqual(LocationError.NotFound,
            await FailureOf(async () => await _session.OpenReadAsync(At("absent"), ReadIntent.Whole, CancellationToken.None)));

    // ── Writing ─────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task AWriteIsInvisibleUntilItCommits()
    {
        await WriteAsync(At("doc.md"), "first");

        await using (var write = await _session.BeginWriteAsync(At("doc.md"), WriteMode.Replace, null, CancellationToken.None))
        {
            await write.Content.WriteAsync(Encoding.UTF8.GetBytes("second"));
            Assert.AreEqual("first", await ReadAsync(At("doc.md")));
        }   // disposed without a commit: discarded

        Assert.AreEqual("first", await ReadAsync(At("doc.md")));
    }

    [TestMethod]
    public async Task ACommittedWriteReplacesTheWholeContent()
    {
        await WriteAsync(At("doc.md"), "a much longer first version");
        await WriteAsync(At("doc.md"), "short");

        Assert.AreEqual("short", await ReadAsync(At("doc.md")));
    }

    [TestMethod]
    public async Task CreateNewOverAnExistingFileIsAConflict()
    {
        await WriteAsync(At("doc.md"), "there");

        var error = await FailureOf(() => WriteAsync(At("doc.md"), Encoding.UTF8.GetBytes("again"), WriteMode.CreateNew));

        Assert.AreEqual(LocationError.Conflict, error);
        Assert.AreEqual("there", await ReadAsync(At("doc.md")));
    }

    [TestMethod]
    public async Task AnAppendAddsToTheEnd()
    {
        await WriteAsync(At("log.txt"), "one;");
        await WriteAsync(At("log.txt"), "two;", WriteMode.Append);

        Assert.AreEqual("one;two;", await ReadAsync(At("log.txt")));
    }

    [TestMethod]
    public async Task AWriteOverAFileThatChangedSinceItWasReadIsAConflict()
    {
        await WriteAsync(At("doc.md"), "as read");
        var asRead = await _session.GetEntryAsync(At("doc.md"), CancellationToken.None);
        await WriteAsync(At("doc.md"), "someone else's edit");

        var error = await FailureOf(() => WriteAsync(At("doc.md"), Encoding.UTF8.GetBytes("mine"), WriteMode.Replace, asRead));

        Assert.AreEqual(LocationError.Conflict, error);
        Assert.AreEqual("someone else's edit", await ReadAsync(At("doc.md")));
    }

    [TestMethod]
    public async Task AWriteOverTheVersionItReadCommits()
    {
        await WriteAsync(At("doc.md"), "as read");
        var asRead = await _session.GetEntryAsync(At("doc.md"), CancellationToken.None);

        await WriteAsync(At("doc.md"), Encoding.UTF8.GetBytes("mine"), WriteMode.Replace, asRead);

        Assert.AreEqual("mine", await ReadAsync(At("doc.md")));
    }

    // ── Moving, deleting, creating ──────────────────────────────────────────

    [TestMethod]
    public async Task ARenameMovesTheFile()
    {
        await WriteAsync(At("old.txt"), "moved");

        await _session.MoveAsync(At("old.txt"), At("new.txt"), replace: false, CancellationToken.None);

        Assert.IsNull(await _session.GetEntryAsync(At("old.txt"), CancellationToken.None));
        Assert.AreEqual("moved", await ReadAsync(At("new.txt")));
    }

    [TestMethod]
    public async Task MovingOntoAnExistingFileIsAConflictUnlessReplacing()
    {
        await WriteAsync(At("a.txt"), "a");
        await WriteAsync(At("b.txt"), "b");

        var error = await FailureOf(async () => await _session.MoveAsync(At("a.txt"), At("b.txt"), replace: false, CancellationToken.None));
        Assert.AreEqual(LocationError.Conflict, error);

        await _session.MoveAsync(At("a.txt"), At("b.txt"), replace: true, CancellationToken.None);
        Assert.AreEqual("a", await ReadAsync(At("b.txt")));
    }

    [TestMethod]
    public async Task RenamingAFolderTakesItsContentsAlong()
    {
        await _session.CreateDirectoryAsync(At("src/deep"), CancellationToken.None);
        await WriteAsync(At("src/deep/x.txt"), "x");

        await _session.MoveAsync(At("src"), At("dst"), replace: false, CancellationToken.None);

        Assert.AreEqual("x", await ReadAsync(At("dst/deep/x.txt")));
        Assert.IsNull(await _session.GetEntryAsync(At("src"), CancellationToken.None));
    }

    [TestMethod]
    public async Task DeletingAMissingPathIsNotFound()
        => Assert.AreEqual(LocationError.NotFound,
            await FailureOf(async () => await _session.DeleteAsync(At("absent"), recursive: false, CancellationToken.None)));

    [TestMethod]
    public async Task AFolderThatHoldsSomethingIsOnlyDeletedRecursively()
    {
        await _session.CreateDirectoryAsync(At("full"), CancellationToken.None);
        await WriteAsync(At("full/x.txt"), "x");

        var error = await FailureOf(async () => await _session.DeleteAsync(At("full"), recursive: false, CancellationToken.None));
        Assert.AreEqual(LocationError.Conflict, error);

        await _session.DeleteAsync(At("full"), recursive: true, CancellationToken.None);
        Assert.IsNull(await _session.GetEntryAsync(At("full"), CancellationToken.None));
    }

    [TestMethod]
    public async Task CreatingAFolderMakesItsParentsAndMayBeRepeated()
    {
        await _session.CreateDirectoryAsync(At("a/b/c"), CancellationToken.None);
        await _session.CreateDirectoryAsync(At("a/b/c"), CancellationToken.None);

        var entry = await _session.GetEntryAsync(At("a/b/c"), CancellationToken.None);
        Assert.IsNotNull(entry);
        Assert.IsTrue(entry.IsDirectory);
    }

    [TestMethod]
    public async Task NamesThatDifferOnlyByCaseAreTwoFilesWhereTheLocationSaysSo()
    {
        if ((_under.Provider.Capabilities & LocationCapabilities.CaseSensitive) == 0)
        {
            Assert.Inconclusive($"{_under.Provider.Kind} names are not case-sensitive.");
            return;
        }
        await WriteAsync(At("Readme"), "upper");
        await WriteAsync(At("readme"), "lower");

        Assert.AreEqual("upper", await ReadAsync(At("Readme")));
        Assert.AreEqual(2, (await ListAsync(_root)).Count);
    }

    // ── Cancellation and the link ───────────────────────────────────────────

    [TestMethod]
    public async Task ACancelledOperationStopsWithoutDoingIt()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await AssertCancelledAsync(async () => await _session.CreateDirectoryAsync(At("never"), cancelled.Token));
        await AssertCancelledAsync(async () =>
        {
            await foreach (var _ in _session.EnumerateAsync(_root, cancelled.Token)) { }
        });

        Assert.IsNull(await _session.GetEntryAsync(At("never"), CancellationToken.None));
    }

    private static async Task AssertCancelledAsync(Func<Task> operation)
    {
        try { await operation(); }
        catch (OperationCanceledException) { return; }
        Assert.Fail("Expected the operation to be cancelled.");
    }

    [TestMethod]
    public async Task AfterTheLinkDropsANewSessionFindsEverythingWhereItWas()
    {
        await WriteAsync(At("kept.txt"), "kept");

        _under.Drop();
        var error = await FailureOf(async () => await _session.GetEntryAsync(At("kept.txt"), CancellationToken.None));
        Assert.AreEqual(LocationError.Disconnected, error);

        await _session.DisposeAsync();
        _session = await _under.Provider.ConnectAsync(_under.Profile, _under.Credentials, CancellationToken.None);
        Assert.AreEqual("kept", await ReadAsync(At("kept.txt")));
    }
}
