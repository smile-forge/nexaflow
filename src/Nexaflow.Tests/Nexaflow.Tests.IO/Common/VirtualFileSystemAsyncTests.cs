using System.IO.Compression;
using System.Text;
using Nexaflow.IO.Common;
using Nexaflow.IO.Common.Locations;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Common;

/// <summary>
/// The async members of the shared file system, over the paths the sync ones already serve: real, mounted and
/// inside an archive. What they add is that nothing waits on the disk on the caller's thread, listings stream,
/// writes land whole or not at all, and a failure with a common meaning arrives as a <see cref="LocationException"/>.
/// </summary>
[TestClass]
[CoversNode("io-vfs-async")]
public sealed class VirtualFileSystemAsyncTests
{
    private VirtualFileSystem _vfs = null!;
    private string _dir = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _vfs = new VirtualFileSystem(TestTemp.CreateSubdirectory("nexa-vfstemp-").FullName);
        _vfs.RegisterHandler(new ZipTestHandler());
        _dir = TestTemp.CreateSubdirectory("nexa-async-").FullName;
        _vfs.RegisterMount(new VirtualMount("docs", "Documents", _dir));
    }

    private string At(string name) => Path.Combine(_dir, name);

    private async Task<List<LocationEntry>> ListAsync(string path, CancellationToken ct = default)
    {
        var entries = new List<LocationEntry>();
        await foreach (var entry in _vfs.EnumerateAsync(path, ct)) entries.Add(entry);
        return entries;
    }

    private static async Task<LocationError> FailureOf(Func<Task> operation)
        => (await Assert.ThrowsExactlyAsync<LocationException>(operation)).Error;

    private static void WriteZipFile(string path, params (string Name, string Body)[] entries)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (name, body) in entries)
            {
                using var s = zip.CreateEntry(name).Open();
                s.Write(Encoding.UTF8.GetBytes(body));
            }
        File.WriteAllBytes(path, ms.ToArray());
    }

    // ── Listing ─────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task AFolderListsEveryChildWithItsFlags()
    {
        File.WriteAllText(At("seen.txt"), "seen");
        File.WriteAllText(At("quiet.txt"), "quiet");
        File.SetAttributes(At("quiet.txt"), FileAttributes.Hidden | FileAttributes.ReadOnly);
        Directory.CreateDirectory(At("sub"));

        var entries = (await ListAsync(_dir)).ToDictionary(e => e.Name);

        Assert.AreEqual(3, entries.Count);
        Assert.IsTrue(entries["sub"].IsDirectory);
        Assert.AreEqual(4, entries["seen.txt"].Size);
        Assert.IsTrue(entries["quiet.txt"].IsHidden);
        Assert.IsTrue(entries["quiet.txt"].IsReadOnly);
        Assert.IsNull(entries["seen.txt"].Archive);
        File.SetAttributes(At("quiet.txt"), FileAttributes.Normal);
    }

    [TestMethod]
    public async Task AMountedFolderListsLikeTheFolderBehindIt()
    {
        File.WriteAllText(At("a.txt"), "a");

        CollectionAssert.AreEqual(new[] { "a.txt" }, (await ListAsync("::docs")).Select(e => e.Name).ToArray());
    }

    [TestMethod]
    public async Task AnArchiveListsItsEntriesWithTheirArchiveFacts()
    {
        WriteZipFile(At("bundle.zip"), ("readme.txt", "hello"), ("src/main.cs", "code"));

        var entries = (await ListAsync(At("bundle.zip"))).ToDictionary(e => e.Name);

        Assert.IsTrue(entries["src"].IsDirectory);
        Assert.AreEqual(5, entries["readme.txt"].Size);
        Assert.IsNotNull(entries["readme.txt"].Archive);
    }

    [TestMethod]
    public async Task ListingAMissingFolderIsNotFound()
        => Assert.AreEqual(LocationError.NotFound, await FailureOf(() => ListAsync(At("absent"))));

    [TestMethod]
    public async Task AReaderThatStopsEarlyDoesNotLeaveTheWalkBehind()
    {
        for (int i = 0; i < 1_000; i++) File.WriteAllText(At($"f{i:D4}.txt"), "x");

        int taken = 0;
        var stopping = Task.Run(async () =>
        {
            await foreach (var _ in _vfs.EnumerateAsync(_dir))
                if (++taken == 3) break;
        });

        Assert.AreSame(stopping, await Task.WhenAny(stopping, Task.Delay(TimeSpan.FromSeconds(10))),
            "stopping after three rows waited on a walk blocked behind a full page");
        Assert.AreEqual(3, taken);
    }

    [TestMethod]
    public async Task ACancelledListingStops()
    {
        File.WriteAllText(At("a.txt"), "a");
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        try
        {
            await ListAsync(_dir, cancelled.Token);
            Assert.Fail("Expected the listing to be cancelled.");
        }
        catch (OperationCanceledException) { }
    }

    // ── Entries and reading ─────────────────────────────────────────────────

    [TestMethod]
    public async Task AnEntryIsReportedForAFileAFolderAndAnArchiveEntry()
    {
        File.WriteAllText(At("a.txt"), "abc");
        WriteZipFile(At("bundle.zip"), ("inner.txt", "zipped"));

        Assert.AreEqual(3, (await _vfs.GetEntryAsync(At("a.txt")))!.Size);
        Assert.IsTrue((await _vfs.GetEntryAsync(_dir))!.IsDirectory);
        Assert.AreEqual(6, (await _vfs.GetEntryAsync(At(@"bundle.zip\inner.txt")))!.Size);
        Assert.IsNull(await _vfs.GetEntryAsync(At("absent.txt")));
        Assert.IsTrue(await _vfs.IsContainerAsync(At("bundle.zip")));
        Assert.IsTrue(await _vfs.IsDirectoryAsync(At("bundle.zip")));
        Assert.IsFalse(await _vfs.ExistsAsync(At("absent.txt")));
    }

    [TestMethod]
    public async Task ARandomReadIsSeekableAndAnArchiveEntryReadsLikeAFile()
    {
        File.WriteAllBytes(At("blob.bin"), Enumerable.Range(0, 1000).Select(i => (byte)i).ToArray());
        WriteZipFile(At("bundle.zip"), ("inner.txt", "zipped"));

        await using (var stream = await _vfs.OpenReadAsync(At("blob.bin"), ReadIntent.Random))
        {
            stream.Seek(500, SeekOrigin.Begin);
            Assert.AreEqual(500 % 256, stream.ReadByte());
        }
        Assert.AreEqual("zipped", await _vfs.ReadAllTextAsync(At(@"bundle.zip\inner.txt")));
    }

    [TestMethod]
    public async Task ReadingAMissingFileIsNotFound()
        => Assert.AreEqual(LocationError.NotFound, await FailureOf(async () => await _vfs.ReadAllBytesAsync(At("absent.bin"))));

    [TestMethod]
    public async Task TextIsReadByItsByteOrderMarkAndWrittenWithTheEncodingsPreamble()
    {
        await _vfs.WriteAllTextAsync(At("u16.txt"), "wide", Encoding.Unicode);

        CollectionAssert.AreEqual(Encoding.Unicode.GetPreamble(), File.ReadAllBytes(At("u16.txt"))[..2]);
        Assert.AreEqual("wide", await _vfs.ReadAllTextAsync(At("u16.txt")));
    }

    // ── Writing ─────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task AWriteIsInvisibleUntilItCommitsAndLeavesNothingWhenAbandoned()
    {
        File.WriteAllText(At("doc.md"), "first");

        await using (var write = await _vfs.BeginWriteAsync(At("doc.md"), WriteMode.Replace))
        {
            await write.Content.WriteAsync(Encoding.UTF8.GetBytes("second"));
            Assert.AreEqual("first", File.ReadAllText(At("doc.md")));
        }

        Assert.AreEqual("first", File.ReadAllText(At("doc.md")));
        CollectionAssert.AreEqual(new[] { "doc.md" }, Directory.GetFiles(_dir).Select(Path.GetFileName).ToArray());
    }

    [TestMethod]
    public async Task ACommittedWriteReplacesTheFileThroughAMount()
    {
        File.WriteAllText(At("doc.md"), "a longer first version");

        await _vfs.ReplaceAsync(@"::docs\doc.md", Encoding.UTF8.GetBytes("short"));

        Assert.AreEqual("short", File.ReadAllText(At("doc.md")));
    }

    [TestMethod]
    public async Task CreateNewOverAnExistingFileIsAConflict()
    {
        File.WriteAllText(At("doc.md"), "there");

        Assert.AreEqual(LocationError.Conflict,
            await FailureOf(async () => await _vfs.BeginWriteAsync(At("doc.md"), WriteMode.CreateNew)));
    }

    [TestMethod]
    public async Task AWriteOverAFileThatChangedSinceItWasReadIsAConflict()
    {
        File.WriteAllText(At("doc.md"), "as read");
        var asRead = await _vfs.GetEntryAsync(At("doc.md"));
        File.WriteAllText(At("doc.md"), "someone else's longer edit");

        await using var write = await _vfs.BeginWriteAsync(At("doc.md"), WriteMode.Replace, asRead);
        await write.Content.WriteAsync(Encoding.UTF8.GetBytes("mine"));

        Assert.AreEqual(LocationError.Conflict, await FailureOf(async () => await write.CommitAsync(CancellationToken.None)));
        Assert.AreEqual("someone else's longer edit", File.ReadAllText(At("doc.md")));
    }

    [TestMethod]
    public async Task AnAppendAddsToTheEnd()
    {
        File.WriteAllText(At("log.txt"), "one;");

        await using (var write = await _vfs.BeginWriteAsync(At("log.txt"), WriteMode.Append))
        {
            await write.Content.WriteAsync(Encoding.UTF8.GetBytes("two;"));
            await write.CommitAsync(CancellationToken.None);
        }

        Assert.AreEqual("one;two;", File.ReadAllText(At("log.txt")));
    }

    // The archive is rewritten only at commit, through its handler — so a format the handler cannot modify refuses
    // there, with nothing changed, rather than leaving a half-written container.
    [TestMethod]
    public async Task AWriteToAnArchiveEntryIsGatheredAndRefusedAtCommitByAReadOnlyFormat()
    {
        WriteZipFile(At("bundle.zip"), ("inner.txt", "zipped"));
        var before = File.ReadAllBytes(At("bundle.zip"));

        await using (var write = await _vfs.BeginWriteAsync(At(@"bundle.zip\inner.txt"), WriteMode.Replace))
        {
            await write.Content.WriteAsync(Encoding.UTF8.GetBytes("rewritten"));
            await Assert.ThrowsExactlyAsync<NotSupportedException>(async () => await write.CommitAsync(CancellationToken.None));
        }

        CollectionAssert.AreEqual(before, File.ReadAllBytes(At("bundle.zip")));
        Assert.AreEqual("zipped", await _vfs.ReadAllTextAsync(At(@"bundle.zip\inner.txt")));
    }

    // ── Moving, deleting, creating ──────────────────────────────────────────

    [TestMethod]
    public async Task AMoveRenamesAFileAndRefusesToLandOnAnotherUnlessReplacing()
    {
        File.WriteAllText(At("a.txt"), "a");
        File.WriteAllText(At("b.txt"), "b");

        Assert.AreEqual(LocationError.Conflict,
            await FailureOf(async () => await _vfs.MoveAsync(At("a.txt"), At("b.txt"), replace: false)));

        await _vfs.MoveAsync(At("a.txt"), At("b.txt"), replace: true);
        Assert.AreEqual("a", File.ReadAllText(At("b.txt")));
        Assert.IsFalse(File.Exists(At("a.txt")));
    }

    [TestMethod]
    public async Task AFolderIsNeverMovedOverAnotherEntry()
    {
        Directory.CreateDirectory(At("src"));
        Directory.CreateDirectory(At("dst"));

        Assert.AreEqual(LocationError.Conflict,
            await FailureOf(async () => await _vfs.MoveAsync(At("src"), At("dst"), replace: true)));
    }

    [TestMethod]
    public async Task AFolderRenamedOnlyInCaseFindsItselfAtTheTargetAndIsNoConflict()
    {
        Directory.CreateDirectory(At("Photos"));

        await _vfs.MoveAsync(At("Photos"), At("photos"), replace: false);

        Assert.AreEqual("photos", new DirectoryInfo(_dir).GetDirectories().Single().Name);
    }

    [TestMethod]
    public async Task AnArchiveEntryIsNotMovedOrDeletedAsAFile()
    {
        WriteZipFile(At("bundle.zip"), ("inner.txt", "zipped"));

        await Assert.ThrowsExactlyAsync<NotSupportedException>(
            async () => await _vfs.DeleteAsync(At(@"bundle.zip\inner.txt"), recursive: false));
        await Assert.ThrowsExactlyAsync<NotSupportedException>(
            async () => await _vfs.MoveAsync(At(@"bundle.zip\inner.txt"), At("out.txt"), replace: false));
    }

    [TestMethod]
    public async Task DeletingNeedsSomethingThereAndRecursionForAFullFolder()
    {
        Directory.CreateDirectory(At("full"));
        File.WriteAllText(At(@"full\x.txt"), "x");

        Assert.AreEqual(LocationError.NotFound,
            await FailureOf(async () => await _vfs.DeleteAsync(At("absent"), recursive: false)));
        Assert.AreEqual(LocationError.Conflict,
            await FailureOf(async () => await _vfs.DeleteAsync(At("full"), recursive: false)));

        await _vfs.DeleteAsync(At("full"), recursive: true);
        Assert.IsFalse(Directory.Exists(At("full")));
    }

    [TestMethod]
    public async Task CreatingAFolderMakesItsParents()
    {
        await _vfs.CreateDirectoryAsync(@"::docs\a\b\c");

        Assert.IsTrue(Directory.Exists(At(@"a\b\c")));
    }

    // ── Whole files ─────────────────────────────────────────────────────────

    [TestMethod]
    public async Task ANewFileHoldsItsBytesAndRefusesToLandOnAnother()
    {
        await _vfs.CreateFileAsync(@"::docs\new.txt", "hello"u8.ToArray());

        Assert.AreEqual("hello", File.ReadAllText(At("new.txt")));
        Assert.AreEqual(LocationError.Conflict,
            await FailureOf(async () => await _vfs.CreateFileAsync(At("new.txt"), "again"u8.ToArray())));
        Assert.AreEqual("hello", File.ReadAllText(At("new.txt")));
    }

    [TestMethod]
    public async Task ACopyCarriesEveryByteAndReplacesOnlyWhenAsked()
    {
        var bytes = new byte[300_000];
        Random.Shared.NextBytes(bytes);
        File.WriteAllBytes(At("big.bin"), bytes);
        File.WriteAllText(At("taken.bin"), "theirs");

        await _vfs.CopyFileAsync(At("big.bin"), @"::docs\copy.bin", replace: false);
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(At("copy.bin")));

        Assert.AreEqual(LocationError.Conflict,
            await FailureOf(async () => await _vfs.CopyFileAsync(At("big.bin"), At("taken.bin"), replace: false)));
        await _vfs.CopyFileAsync(At("big.bin"), At("taken.bin"), replace: true);
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(At("taken.bin")));
    }

    [TestMethod]
    public async Task AnArchiveEntryCopiesOutLikeAFile()
    {
        WriteZipFile(At("a.zip"), ("inner.txt", "inside"));

        await _vfs.CopyFileAsync(Path.Combine(At("a.zip"), "inner.txt"), At("out.txt"), replace: false);

        Assert.AreEqual("inside", File.ReadAllText(At("out.txt")));
    }

    // ── Companions ──────────────────────────────────────────────────────────

    [TestMethod]
    public async Task ACompanionFollowsItsFileThroughAMountAndAStaleOneAtTheTargetGoes()
    {
        File.WriteAllText(At("photo.jpg.nfedit"), "edits");
        File.WriteAllText(At("other.jpg.nfedit"), "stale");
        File.SetAttributes(At("other.jpg.nfedit"), FileAttributes.Hidden);

        var failed = await CompanionFiles.FollowAsync(_vfs, @"::docs\photo.jpg", @"::docs\other.jpg", [".nfedit"], move: true);

        Assert.AreEqual(0, failed.Count);
        Assert.IsFalse(File.Exists(At("photo.jpg.nfedit")));
        Assert.AreEqual("edits", File.ReadAllText(At("other.jpg.nfedit")));
    }

    [TestMethod]
    public async Task WhereTheSourceHasNoCompanionOneAtTheTargetDescribedTheReplacedFileAndGoes()
    {
        File.WriteAllText(At("other.jpg.nfedit"), "stale");

        await CompanionFiles.FollowAsync(_vfs, At("photo.jpg"), At("other.jpg"), [".nfedit"], move: false);

        Assert.IsFalse(File.Exists(At("other.jpg.nfedit")));
    }

    // ── Watching and capabilities ───────────────────────────────────────────

    [TestMethod]
    public async Task AWatchReportsAChangeByThePathItWasAskedWith()
    {
        var reported = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var watch = await _vfs.WatchChangesAsync("::docs", recursive: false);
        Assert.IsNotNull(watch);
        watch.Changed += path => reported.TrySetResult(path);

        File.WriteAllText(At("new.txt"), "new");

        var first = await Task.WhenAny(reported.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.AreSame(reported.Task, first, "no change was reported");
        Assert.AreEqual(@"::docs\new.txt", reported.Task.Result);
    }

    [TestMethod]
    public void AnArchiveEntryCanOnlyBeReadAtRandom()
    {
        WriteZipFile(At("bundle.zip"), ("inner.txt", "zipped"));

        Assert.AreEqual(LocationCapabilities.RandomRead, _vfs.GetCapabilities(At(@"bundle.zip\inner.txt")));
        Assert.IsTrue((_vfs.GetCapabilities(At("bundle.zip")) & LocationCapabilities.AtomicRename) != 0);
    }

    [TestMethod]
    public void ALocalPathIsItsOwnDisplayForm()
    {
        Assert.AreEqual(_dir, _vfs.GetDisplayPath(_dir));
        Assert.IsNull(_vfs.TryParseDisplayPath("sftp://nas/home"));
    }
}
