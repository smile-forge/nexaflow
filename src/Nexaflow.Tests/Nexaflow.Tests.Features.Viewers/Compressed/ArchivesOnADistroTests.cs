using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using Nexaflow.Features.Common;
using Nexaflow.Features.Compressed;
using Nexaflow.Features.Compressed.FileActions;
using Nexaflow.Features.Compressed.Handlers;
using Nexaflow.IO.Common;
using Nexaflow.IO.Common.Locations;
using Nexaflow.IO.Location.Wsl;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;
using NSubstitute;

namespace Nexaflow.Tests.Features.Compressed;

/// <summary>
/// Archives in a real WSL distro, as a person meets them: Zip It and Unzip Here, an archive opened and changed inside, and a
/// Markdown Package saved and opened. Each change lands in the distro under the names Linux gives its files — one of them
/// a name Windows could not hold — and an archive changed in place keeps its permissions. Everything is the test's own,
/// under a folder it makes in the distro's <c>/tmp</c> and removes.
/// </summary>
[TestClass]
[DoNotParallelize]
[TestCategory("Wsl")]
[CoversNode("compressed")]
public sealed class ArchivesOnADistroTests
{
    private const string LinuxOnly = "at 12:30.txt";

    private sealed class NoSecrets : ISecretStore
    {
        public string? Read(string target) => null;
        public void Write(string target, string? userName, string secret) { }
        public bool Delete(string target) => false;
    }

    private LocationSessionPool _pool = null!;
    private LocationSet _set = null!;
    private LocationFileSystem _files = null!;
    private string _distro = string.Empty;
    private string _name = string.Empty;
    private string _folder = string.Empty;

    [TestInitialize]
    public async Task Setup()
    {
        if (!VirtualFileSystem.Instance.CanCreate("a.zip"))
            VirtualFileSystem.Instance.RegisterHandler(new ZipArchiveHandler());

        var provider = new WslLocationProvider();
        var distros  = await provider.DiscoverAsync(CancellationToken.None);
        if (distros.Count == 0) Assert.Inconclusive("WSL has no distro installed on this machine.");
        _distro = distros[0].Host;

        var registry = new LocationProviderRegistry();
        registry.Register(provider);
        _pool  = new LocationSessionPool(registry, new StoredCredentialSource(new NoSecrets()));
        _set   = _pool.Acquire([distros[0]]);
        _files = new LocationFileSystem(VirtualFileSystem.Instance);
        _files.RegisterLocation(_set.Mounts[0]);

        _name   = $"nexaflow-archives-{Guid.NewGuid():N}";
        _folder = $@"{_set.Mounts[0].Root}\tmp\{_name}";
        await _files.CreateDirectoryAsync(_folder);
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        if (_files is null) return;
        try { await _files.DeleteAsync(_folder, recursive: true); } catch { /* best effort */ }
        _pool.Release(_set);
        await _pool.DisposeAsync();
    }

    private string At(params string[] names) => names.Aggregate(_folder, (path, name) => _files.ChildPath(path, name));

    [TestMethod]
    [Timeout(180_000, CooperativeCancellation = true)]
    public async Task ZipItThenUnzipHere_KeepTheNamesLinuxGaveTheFiles()
    {
        await _files.CreateDirectoryAsync(At("src"));
        await _files.WriteAllTextAsync(At("src", "a.txt"), "alpha");
        await _files.WriteAllTextAsync(At("src", LinuxOnly), "half past");
        var shell = Substitute.For<IShellServices>();

        Assert.IsTrue(await new ZipItAction(shell, new CompressedConfig(), _files).PerformActionAsync(At("src")));
        Assert.IsTrue(await _files.ExistsAsync(At("src.zip")), "the archive is made beside the folder");
        CollectionAssert.AreEquivalent(new[] { "a.txt", LinuxOnly }, ZipNames(await _files.ReadAllBytesAsync(At("src.zip"))),
                                       "the archive names each file as Linux does");

        Assert.IsTrue(await new UnzipHereAction(shell, _files).PerformActionAsync(At("src.zip")));
        shell.DidNotReceiveWithAnyArgs().ShowError(default!);
        CollectionAssert.AreEquivalent(new[] { "a.txt", LinuxOnly }, (await InDistroAsync("ls", "-1", $"/tmp/{_name}/src (1)")).Split('\n', StringSplitOptions.RemoveEmptyEntries));
        Assert.AreEqual("half past", await InDistroAsync("cat", $"/tmp/{_name}/src (1)/{LinuxOnly}"));
    }

    [TestMethod]
    [Timeout(180_000, CooperativeCancellation = true)]
    public async Task AnArchiveChangedInside_KeepsItsPermissions()
    {
        await _files.WriteAllBytesAsync(At("book.zip"), Zip(("a.txt", "alpha"), ("b.txt", "beta")));
        await InDistroAsync("chmod", "640", $"/tmp/{_name}/book.zip");
        var before = (await _files.GetEntryAsync(At("book.zip")))!;
        var book   = At("book.zip");

        var listed = new List<string>();
        await foreach (var entry in _files.EnumerateAsync(book)) listed.Add(entry.Name);
        CollectionAssert.AreEquivalent(new[] { "a.txt", "b.txt" }, listed, "browsed like a folder");

        await _files.WriteAllTextAsync(Path.Combine(book, "a.txt"), "changed");

        var added = Path.Combine(TestTemp.CreateSubdirectory("nexa-distro-add-").FullName, "c.txt");
        await File.WriteAllTextAsync(added, "gamma");
        await _files.AddFilesAsync(book, [(added, "c.txt")]);

        var entries = ZipEntries(await _files.ReadAllBytesAsync(book));
        CollectionAssert.AreEquivalent(new[] { "a.txt", "b.txt", "c.txt" }, entries.Keys.ToArray());
        Assert.AreEqual("changed", entries["a.txt"]);
        var after = (await _files.GetEntryAsync(book))!;
        Assert.AreEqual(before.Mode, after.Mode, "rw-r----- stays rw-r-----");
        Assert.AreEqual(before.Owner, after.Owner);
    }

    [TestMethod]
    [Timeout(180_000, CooperativeCancellation = true)]
    public async Task AMarkdownPackageSavedAndOpened_InTheDistro()
    {
        await _files.WriteAllBytesAsync(At("pic.png"), [0x89, 0x50, 0x4E, 0x47]);
        var package = At("note.mdz");
        await MarkdownPackage.PackAsync(_files, "# Note\n\n![pic](pic.png)\n", _folder, package);
        await InDistroAsync("chmod", "600", $"/tmp/{_name}/note.mdz");
        var before = (await _files.GetEntryAsync(package))!;

        await MarkdownPackage.PackAsync(_files, "# Note, again\n\n![pic](pic.png)\n", _folder, package);

        var after = (await _files.GetEntryAsync(package))!;
        Assert.AreEqual(before.Mode, after.Mode, "a saved package keeps rw-------");
        var opened = TestTemp.CreateSubdirectory("nexa-distro-mdz-").FullName;
        var text = await MarkdownPackage.UnpackAsync(_files, package, opened);
        StringAssert.StartsWith(text, "# Note, again");
        Assert.IsTrue(File.Exists(Path.Combine(opened, MarkdownPackage.Attachments, "pic.png")), "the picture went with it");
    }

    private static byte[] Zip(params (string Name, string Text)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (name, text) in entries)
            {
                using var stream = zip.CreateEntry(name).Open();
                stream.Write(Encoding.UTF8.GetBytes(text));
            }
        return buffer.ToArray();
    }

    private static string[] ZipNames(byte[] archive) => [.. ZipEntries(archive).Keys];

    private static Dictionary<string, string> ZipEntries(byte[] archive)
    {
        using var zip = new ZipArchive(new MemoryStream(archive), ZipArchiveMode.Read);
        return zip.Entries.Where(e => e.Name.Length > 0)
                  .ToDictionary(e => e.FullName, e => new StreamReader(e.Open()).ReadToEnd());
    }

    private async Task<string> InDistroAsync(params string[] arguments)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "wsl.exe"))
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, StandardOutputEncoding = Encoding.UTF8,
        };
        foreach (var argument in (string[])["-d", _distro, "--exec", .. arguments]) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.AreEqual(0, process.ExitCode, string.Join(' ', arguments));
        return output.TrimEnd('\n');
    }
}
