using System.Text;
using Nexaflow.IO.Common.Locations;
using Nexaflow.IO.Common.Terminal;
using Nexaflow.IO.Location.Wsl;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Location.Wsl;

/// <summary>
/// Names a distro holds that are no UTF-8 — <c>caf</c> and a stray 0xE9 — which WSL's file server shows as U+FFFD and
/// cannot open. Through the helper each is listed, read, written, renamed, copied, watched, run in and deleted by its exact
/// bytes, in the default distro, in a folder of its own under <c>/tmp</c>.
/// </summary>
[TestClass]
[DoNotParallelize]
[TestCategory("Interactive")]
[TestCategory("Wsl")]
[CoversNode("io-locations-wsl-names")]
public sealed class WslStrayNameTests
{
    private static readonly string Cafe = PosixNames.FromBytes([0x63, 0x61, 0x66, 0xE9, 0x2E, 0x74, 0x78, 0x74]);   // caf\xE9.txt
    private static readonly string Odd  = PosixNames.FromBytes([0x6F, 0x64, 0x64, 0xFF, 0xFE]);                   // odd\xFF\xFE

    private WslDistro _distro = null!;
    private ILocationSession _session = null!;
    private string _folder = string.Empty;

    [TestInitialize]
    public async Task Setup()
    {
        _distro  = WslUnderTest.Distro();
        _session = await new WslLocationProvider().ConnectAsync(WslLocationProvider.ConnectionFor(_distro), null!, CancellationToken.None);
        _folder  = $"/tmp/nexaflow-names-{Guid.NewGuid():N}";
        await _session.CreateDirectoryAsync(_folder, CancellationToken.None);
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        if (_session is null) return;
        try { await _session.DeleteAsync(_folder, recursive: true, CancellationToken.None); } catch { /* best effort */ }
        await _session.DisposeAsync();
    }

    private string In(string name) => $"{_folder}/{name}";

    // Made inside Linux, by its bytes, so nothing of this backend made it.
    private Task MakeInLinuxAsync(string content)
        => WslUnderTest.RunInDistroAsync(_distro.Name, "sh", "-c", $"printf '{content}' > \"$(printf '{_folder}/caf\\351.txt')\"");

    private async Task<string> ReadAsync(string path)
    {
        await using var read = await _session.OpenReadAsync(path, ReadIntent.Whole, CancellationToken.None);
        return await new StreamReader(read).ReadToEndAsync();
    }

    private async Task WriteAsync(string path, WriteMode mode, string content)
    {
        await using var write = await _session.BeginWriteAsync(path, mode, null, null, CancellationToken.None);
        await write.Content.WriteAsync(Encoding.UTF8.GetBytes(content));
        await write.CommitAsync(CancellationToken.None);
    }

    [TestMethod]
    public async Task AFileMadeInLinux_IsListedReadWrittenRenamedCopiedAndDeleted_ByItsBytes()
    {
        await MakeInLinuxAsync("from linux");

        var listed = new List<string>();
        await foreach (var entry in _session.EnumerateAsync(_folder, CancellationToken.None)) listed.Add(entry.Name);
        CollectionAssert.AreEqual(new[] { Cafe }, listed, "listed by its exact bytes, not with U+FFFD in its place");

        Assert.AreEqual("from linux", await ReadAsync(In(Cafe)));

        await WriteAsync(In(Cafe), WriteMode.Replace, "from nexaflow");
        Assert.AreEqual("from nexaflow", await ReadAsync(In(Cafe)));
        var said = await WslHelperDeployment.RunAsync(_distro.Name, CancellationToken.None,
                                                      "--exec", "sh", "-c", $"cat \"$(printf '{_folder}/caf\\351.txt')\"");
        StringAssert.Contains(said, "from nexaflow", "Linux reads what was written, under the one name");

        await _session.MoveAsync(In(Cafe), In(Odd), replace: false, CancellationToken.None);
        Assert.IsNull(await _session.GetEntryAsync(In(Cafe), CancellationToken.None));
        Assert.IsNotNull(await _session.GetEntryAsync(In(Odd), CancellationToken.None));

        await _session.CopyWithinAsync(In(Odd), In(Cafe), replace: false, CancellationToken.None);
        Assert.AreEqual("from nexaflow", await ReadAsync(In(Cafe)));

        await _session.DeleteAsync(In(Cafe), recursive: false, CancellationToken.None);
        await _session.DeleteAsync(In(Odd), recursive: false, CancellationToken.None);
        var left = new List<string>();
        await foreach (var entry in _session.EnumerateAsync(_folder, CancellationToken.None)) left.Add(entry.Name);
        Assert.AreEqual(0, left.Count);
    }

    [TestMethod]
    public async Task AFolderSoNamed_IsMadeFilledReadAndDeletedWhole()
    {
        var folder = In(Odd);
        await _session.CreateDirectoryAsync(folder, CancellationToken.None);
        await WriteAsync($"{folder}/{Cafe}", WriteMode.CreateNew, "inside");
        await WriteAsync($"{folder}/{Cafe}", WriteMode.Append, ", appended");

        Assert.AreEqual("inside, appended", await ReadAsync($"{folder}/{Cafe}"));

        await _session.DeleteAsync(folder, recursive: true, CancellationToken.None);
        Assert.IsNull(await _session.GetEntryAsync(folder, CancellationToken.None));
    }

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task AProgramRunsInAFolderSoNamed_GivenSuchANameWhole()
    {
        var folder = In(Odd);
        await _session.CreateDirectoryAsync(folder, CancellationToken.None);
        await WriteAsync($"{folder}/{Cafe}", WriteMode.CreateNew, "x");

        var found = await _session.Shell!.RunProgramAsync(new ProgramRun("test", ["-f", Cafe], folder), null, CancellationToken.None);
        var other = await _session.Shell!.RunProgramAsync(new ProgramRun("test", ["-f", "caf�.txt"], folder), null, CancellationToken.None);

        Assert.IsTrue(found.Succeeded, "run in the folder by its bytes, and handed the name by its bytes");
        Assert.IsFalse(other.Succeeded, "the name as Windows shows it is another name");
    }

    [TestMethod]
    public async Task AChangeToAFileSoNamed_IsReportedByItsBytes()
    {
        var seen = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var watch = _session.Watch(_folder, recursive: true)!;
        watch.Changed += path => { if (path != _folder) seen.TrySetResult(path); };
        await Task.Delay(500);   // the helper answers the watch before it reports

        await MakeInLinuxAsync("watched");

        Assert.AreEqual(In(Cafe), await seen.Task.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [TestMethod]
    public async Task AShellStartsInAFolderSoNamed()
    {
        var folder = In(Odd) + "-end";
        await _session.CreateDirectoryAsync(folder, CancellationToken.None);

        var output = new StringBuilder();
        var reported = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var transport = await _session.Shell!.LaunchAsync(new TerminalLaunch(folder, null, 120, 30), CancellationToken.None);
        try
        {
            transport.Output += chunk =>
            {
                lock (output)
                {
                    output.Append(Encoding.UTF8.GetString(chunk.Span));
                    if (output.ToString().Contains("\u001b]7;file://", StringComparison.Ordinal)) reported.TrySetResult();
                }
            };
            await reported.Task.WaitAsync(TimeSpan.FromSeconds(20));

            lock (output) StringAssert.Contains(output.ToString(), "-end\u001b\\", "the shell reports the folder it started in");
        }
        finally
        {
            transport.TerminateForeground();
            (transport as IDisposable)?.Dispose();
        }
    }
}
