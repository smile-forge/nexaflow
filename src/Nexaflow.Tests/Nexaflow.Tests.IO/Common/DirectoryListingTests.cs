using System;
using System.IO;
using System.Linq;
using Nexaflow.IO.Common.Cloud;
using Nexaflow.IO.Common.Listing;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Common;

/// <summary>
/// A directory read straight from the file system: the same entries, sizes and times <see cref="DirectoryInfo"/> gives,
/// failing the way it fails — and each entry's cloud sync state read from the listing itself, as Explorer reads it.
/// </summary>
[TestClass]
[CoversNode("iocommon-directory-listing")]
public class DirectoryListingTests
{
    private const FileAttributes Pinned             = (FileAttributes)0x00080000;
    private const FileAttributes RecallOnDataAccess = (FileAttributes)0x00400000;

    private string _folder = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _folder = TestTemp.CreateSubdirectory("listing-").FullName;
        Directory.CreateDirectory(Path.Combine(_folder, "sub"));
        File.WriteAllText(Path.Combine(_folder, "notes.txt"), "twelve bytes");
        File.WriteAllBytes(Path.Combine(_folder, "empty.bin"), []);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    [TestMethod]
    public void ItListsWhatDirectoryInfoLists_NameForName()
    {
        var listed = DirectoryListing.Enumerate(_folder).ToDictionary(entry => entry.Name);
        var known  = new DirectoryInfo(_folder).EnumerateFileSystemInfos().ToList();

        Assert.AreEqual(known.Count, listed.Count, "and nothing else — not '.' or '..'");
        foreach (var info in known)
        {
            var entry = listed[info.Name];
            Assert.AreEqual(info is DirectoryInfo, entry.IsDirectory, info.Name);
            Assert.AreEqual(info.LastWriteTime, entry.LastWriteTime, info.Name);
            if (info is FileInfo file) Assert.AreEqual(file.Length, entry.Size, info.Name);
        }
    }

    [TestMethod]
    public void OutsideASyncedFolder_NothingIsACloudFile()
    {
        Assert.IsTrue(DirectoryListing.Enumerate(_folder).All(entry => entry.CloudState == CloudFileState.None));
        Assert.IsFalse(CloudPlaceholders.IsInSyncRoot(_folder));
    }

    [TestMethod]
    public void AFolderThatIsNotThere_FailsAsDirectoryInfoWould()
    {
        var missing = Path.Combine(_folder, "nowhere");

        Assert.ThrowsExactly<DirectoryNotFoundException>(() => DirectoryListing.Enumerate(missing).ToList());
    }

    // ── What a listing's placeholder state says ─────────────────────────────────

    [TestMethod]
    public void AnOrdinaryFile_IsNoCloudFile() =>
        Assert.AreEqual(CloudFileState.None, CloudPlaceholders.Classify(FileAttributes.Archive, CloudPlaceholderFlags.None));

    [TestMethod]
    public void APlaceholderInStep_IsAvailable_OrAlwaysAvailableWhenKeptHere()
    {
        var inSync = CloudPlaceholderFlags.Placeholder | CloudPlaceholderFlags.InSync;

        Assert.AreEqual(CloudFileState.Available, CloudPlaceholders.Classify(FileAttributes.Archive, inSync));
        Assert.AreEqual(CloudFileState.AlwaysAvailable, CloudPlaceholders.Classify(FileAttributes.Archive | Pinned, inSync));
    }

    [TestMethod]
    public void APlaceholderNotOnDisk_IsOnlineOnly_OrOnItsWayDownWhenKeptHere()
    {
        var notHere = CloudPlaceholderFlags.Placeholder | CloudPlaceholderFlags.InSync
                    | CloudPlaceholderFlags.Partial | CloudPlaceholderFlags.PartiallyOnDisk;

        Assert.AreEqual(CloudFileState.OnlineOnly, CloudPlaceholders.Classify(RecallOnDataAccess, notHere));
        Assert.AreEqual(CloudFileState.PendingDownload, CloudPlaceholders.Classify(RecallOnDataAccess | Pinned, notHere));
    }

    [TestMethod]
    public void APlaceholderOutOfStep_IsSyncPending() =>
        Assert.AreEqual(CloudFileState.SyncPending,
                        CloudPlaceholders.Classify(FileAttributes.Archive, CloudPlaceholderFlags.Placeholder));

    // ── What a provider's own word says ─────────────────────────────────────────

    [TestMethod]
    public void EveryProviderStateIsNamed_AndAnUnknownOneIsNothing()
    {
        CloudFileState[] expected =
        [
            CloudFileState.None, CloudFileState.OnlineOnly, CloudFileState.Available, CloudFileState.AlwaysAvailable,
            CloudFileState.PendingUpload, CloudFileState.PendingDownload, CloudFileState.Transferring, CloudFileState.Error,
            CloudFileState.Warning, CloudFileState.Excluded, CloudFileState.SyncPending,
        ];
        for (uint value = 0; value < expected.Length; value++)
            Assert.AreEqual(expected[value], CloudProviderStatus.StateFrom(value), $"STORAGEPROVIDERSTATE {value}");

        Assert.AreEqual(CloudFileState.None, CloudProviderStatus.StateFrom(99));
    }

    [TestMethod]
    public void SharedAndPublicAreTold_PrivateIsNotShared()
    {
        Assert.AreEqual(CloudSharing.None, CloudProviderStatus.SharingFrom(0));
        Assert.AreEqual(CloudSharing.None, CloudProviderStatus.SharingFrom(2));
        foreach (uint shared in new uint[] { 1, 4, 5 }) Assert.AreEqual(CloudSharing.Shared, CloudProviderStatus.SharingFrom(shared));
        foreach (uint open in new uint[] { 3, 6, 7 }) Assert.AreEqual(CloudSharing.Public, CloudProviderStatus.SharingFrom(open));
    }
}
