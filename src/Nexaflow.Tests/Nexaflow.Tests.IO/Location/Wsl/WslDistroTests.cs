using System.Diagnostics;
using System.Text;
using Nexaflow.IO.Common.Locations;
using Nexaflow.IO.Common.Terminal;
using Nexaflow.IO.Location.Wsl;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Location.Wsl;

/// <summary>
/// The WSL backend against this machine's default distro: the location contract, run in a folder under <c>/tmp</c>
/// (the contract works at the root, which only root may write), and what only a real distro can show — owners read by
/// name, a change made inside Linux reaching a watch, and the shell reporting its folder. Interactive: a distro is
/// started, so these run on a developer's machine, never in CI; Inconclusive where WSL has no distro.
/// </summary>
internal static class WslUnderTest
{
    public const string Base = "/tmp/nexaflow-contract";

    /// <summary>The default distro, or Inconclusive.</summary>
    public static WslDistro Distro()
        => WslRegistry.Read().FirstOrDefault(d => d.IsDefault) ?? WslRegistry.Read().FirstOrDefault()
           ?? throw new AssertInconclusiveException("WSL has no distro installed on this machine.");

    /// <summary>Runs a command in the distro, as a person would from its own shell.</summary>
    public static async Task RunInDistroAsync(string distro, params string[] arguments)
        => await WslHelperDeployment.RunAsync(distro, CancellationToken.None, ["--exec", .. arguments]);
}

[TestClass]
[TestCategory("Interactive")]
[TestCategory("Wsl")]
[CoversNode("io-locations-wsl")]
public sealed class WslLocationContractTests : LocationSessionContract
{
    protected override LocationUnderTest Connect()
    {
        var distro   = WslUnderTest.Distro();
        var provider = new UnderBase(new WslLocationProvider());
        return new LocationUnderTest(provider, WslLocationProvider.ConnectionFor(distro), new StoredCredentials(null), provider.Drop);
    }


    /// <summary>The WSL provider with every path under <see cref="WslUnderTest.Base"/>, and a way to drop its helper.</summary>
    private sealed class UnderBase(WslLocationProvider wsl) : ILocationProvider
    {
        private WslSession? _last;

        public string Kind => wsl.Kind;

        public LocationCapabilities Capabilities => wsl.Capabilities;

        public async ValueTask<ILocationSession> ConnectAsync(ConnectionProfile profile, ICredentialSource credentials, CancellationToken ct)
        {
            var session = (WslSession)await wsl.ConnectAsync(profile, credentials, ct);
            _last = session;
            return new Rooted(session);
        }

        public void Drop() => _last?.Link.Kill();
    }

    private sealed class Rooted(ILocationSession inner) : ILocationSession
    {
        private static string In(string path) => WslUnderTest.Base + (path == "/" ? "" : path);

        private static string Out(string path) => path.StartsWith(WslUnderTest.Base, StringComparison.Ordinal) ? path[WslUnderTest.Base.Length..] : path;

        public IAsyncEnumerable<LocationEntry> EnumerateAsync(string path, CancellationToken ct) => inner.EnumerateAsync(In(path), ct);
        public async ValueTask<LocationEntry?> GetEntryAsync(string path, CancellationToken ct)
        {
            var entry = await inner.GetEntryAsync(In(path), ct);
            return entry is not null && path == "/" ? entry with { Name = string.Empty } : entry;   // the base is this root
        }
        public ValueTask<Stream> OpenReadAsync(string path, ReadIntent intent, CancellationToken ct) => inner.OpenReadAsync(In(path), intent, ct);
        public ValueTask<ILocationWrite> BeginWriteAsync(string path, WriteMode mode, LocationEntry? expected, UnixFileMode? permissions, CancellationToken ct)
            => inner.BeginWriteAsync(In(path), mode, expected, permissions, ct);
        public ValueTask MoveAsync(string from, string to, bool replace, CancellationToken ct) => inner.MoveAsync(In(from), In(to), replace, ct);
        public ValueTask CopyWithinAsync(string from, string to, bool replace, CancellationToken ct) => inner.CopyWithinAsync(In(from), In(to), replace, ct);
        public ValueTask DeleteAsync(string path, bool recursive, CancellationToken ct) => inner.DeleteAsync(In(path), recursive, ct);
        public ValueTask CreateDirectoryAsync(string path, CancellationToken ct) => inner.CreateDirectoryAsync(In(path), ct);

        public ILocationWatch? Watch(string path, bool recursive)
            => inner.Watch(In(path), recursive) is { } watch ? new RootedWatch(watch, Out) : null;

        public ILocationShell? Shell => inner.Shell;

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }

    private sealed class RootedWatch : ILocationWatch
    {
        private readonly ILocationWatch _inner;

        public RootedWatch(ILocationWatch inner, Func<string, string> map)
        {
            _inner = inner;
            _inner.Changed += path => Changed?.Invoke(map(path));
        }

        public event Action<string>? Changed;

        public void Dispose() => _inner.Dispose();
    }
}

[TestClass]
[TestCategory("Interactive")]
[TestCategory("Wsl")]
[CoversNode("io-locations-wsl")]
public sealed class WslDistroTests
{
    private WslDistro _distro = null!;
    private ILocationSession _session = null!;

    [TestInitialize]
    public async Task Connect()
    {
        _distro  = WslUnderTest.Distro();
        _session = await new WslLocationProvider().ConnectAsync(WslLocationProvider.ConnectionFor(_distro), null!, CancellationToken.None);
    }

    [TestCleanup]
    public async Task Close() => await _session.DisposeAsync();

    [TestMethod]
    public async Task AFolderIsListedWithItsOwnerAndPermissions_ByName()
    {
        var etc = new List<LocationEntry>();
        await foreach (var entry in _session.EnumerateAsync("/etc", CancellationToken.None)) etc.Add(entry);

        var hosts = etc.Single(e => e.Name == "hosts");
        Assert.AreEqual("root", hosts.Owner);
        Assert.AreEqual("root", hosts.Group);
        Assert.IsTrue(hosts.Mode!.Value.HasFlag(UnixFileMode.UserRead));
    }

    [TestMethod]
    public async Task AChangeMadeInsideLinux_ReachesTheWatch()
    {
        var folder = $"{WslUnderTest.Base}/watch-{Guid.NewGuid():N}";
        await _session.CreateDirectoryAsync(folder, CancellationToken.None);
        try
        {
            var seen = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var watch = _session.Watch(folder, recursive: true)!;
            watch.Changed += path => seen.TrySetResult(path);
            await Task.Delay(500);   // the helper answers the watch before it reports

            await WslUnderTest.RunInDistroAsync(_distro.Name, "touch", $"{folder}/made-in-linux");

            var changed = await seen.Task.WaitAsync(TimeSpan.FromSeconds(10));
            StringAssert.StartsWith(changed, folder);
        }
        finally
        {
            await _session.DeleteAsync(folder, recursive: true, CancellationToken.None);
        }
    }

    [TestMethod]
    public async Task ASaveOverAScript_KeepsItRunnable()
    {
        var script = $"{WslUnderTest.Base}/run-{Guid.NewGuid():N}.sh";
        await WslUnderTest.RunInDistroAsync(_distro.Name, "sh", "-c", $"mkdir -p {WslUnderTest.Base} && printf 'echo one\\n' > {script} && chmod 755 {script}");
        try
        {
            var read = await _session.GetEntryAsync(script, CancellationToken.None);
            await using (var write = await _session.BeginWriteAsync(script, WriteMode.Replace, read, null, CancellationToken.None))
            {
                await write.Content.WriteAsync(Encoding.UTF8.GetBytes("echo two\n"));
                await write.CommitAsync(CancellationToken.None);
            }

            var saved = await _session.GetEntryAsync(script, CancellationToken.None);
            Assert.IsTrue(saved!.Mode!.Value.HasFlag(UnixFileMode.UserExecute), "a save renamed over the script keeps its permissions");
        }
        finally
        {
            await _session.DeleteAsync(script, recursive: false, CancellationToken.None);
        }
    }

    [TestMethod]
    public async Task TheShellReportsTheFolderItIsIn()
    {
        var output = new StringBuilder();
        var reported = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var transport = await _session.Shell!.LaunchAsync(new TerminalLaunch("/tmp", null, 120, 30), CancellationToken.None);
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

            lock (output) StringAssert.Contains(output.ToString(), "/tmp\u001b\\");
        }
        finally
        {
            transport.TerminateForeground();
            (transport as IDisposable)?.Dispose();
        }
    }

    [TestMethod]
    public async Task ANameWindowsWouldTrim_IsReadAndSavedAsItself()
    {
        var folder = $"{WslUnderTest.Base}/exact-{Guid.NewGuid():N}";
        await _session.CreateDirectoryAsync(folder, CancellationToken.None);
        try
        {
            await WslUnderTest.RunInDistroAsync(_distro.Name, "sh", "-c",
                $"printf plain > {folder}/notes && printf dotted > {folder}/notes. && printf spaced > '{folder}/notes '");

            foreach (var (name, content) in new[] { ("notes.", "dotted"), ("notes ", "spaced") })
            {
                await using var stream = await _session.OpenReadAsync($"{folder}/{name}", ReadIntent.Whole, CancellationToken.None);
                using var reader = new StreamReader(stream);
                Assert.AreEqual(content, await reader.ReadToEndAsync(), $"'{name}' is not 'notes'");
            }

            await using (var write = await _session.BeginWriteAsync($"{folder}/notes.", WriteMode.Append, null, null, CancellationToken.None))
            {
                await write.Content.WriteAsync(Encoding.UTF8.GetBytes("+more"));
                await write.CommitAsync(CancellationToken.None);
            }
            await using var plain = await _session.OpenReadAsync($"{folder}/notes", ReadIntent.Whole, CancellationToken.None);
            using var unchanged = new StreamReader(plain);
            Assert.AreEqual("plain", await unchanged.ReadToEndAsync(), "an append to 'notes.' never lands in 'notes'");
        }
        finally
        {
            await _session.DeleteAsync(folder, recursive: true, CancellationToken.None);
        }
    }

    private static UnixFileMode Octal(string bits) => (UnixFileMode)Convert.ToInt32(bits, 8);

    [TestMethod]
    [CoversNode("io-locations-transfer-permissions")]
    public async Task AFileAWriteMakes_HasTheBitsAskedOfIt_EvenWithoutItsOwnersWrite()
    {
        var file = $"{WslUnderTest.Base}/made-{Guid.NewGuid():N}";
        await WslUnderTest.RunInDistroAsync(_distro.Name, "mkdir", "-p", WslUnderTest.Base);
        try
        {
            await using (var write = await _session.BeginWriteAsync(file, WriteMode.CreateNew, null, Octal("440"), CancellationToken.None))
            {
                await write.Content.WriteAsync(Encoding.UTF8.GetBytes("read only"));
                await write.CommitAsync(CancellationToken.None);
            }

            Assert.AreEqual(Octal("440"), (await _session.GetEntryAsync(file, CancellationToken.None))!.Mode,
                            "made with exactly the bits asked of it, though it was written under its owner's write");
            await using var read = await _session.OpenReadAsync(file, ReadIntent.Whole, CancellationToken.None);
            Assert.AreEqual("read only", await new StreamReader(read).ReadToEndAsync());
        }
        finally
        {
            await _session.DeleteAsync(file, recursive: false, CancellationToken.None);
        }
    }

    [TestMethod]
    [CoversNode("io-locations-transfer-permissions")]
    public async Task AWriteOverAFile_KeepsItsBits_WhateverWasAsked()
    {
        var file = $"{WslUnderTest.Base}/kept-{Guid.NewGuid():N}";
        await WslUnderTest.RunInDistroAsync(_distro.Name, "sh", "-c", $"mkdir -p {WslUnderTest.Base} && printf old > {file} && chmod 640 {file}");
        try
        {
            await using (var write = await _session.BeginWriteAsync(file, WriteMode.Replace, null, Octal("777"), CancellationToken.None))
            {
                await write.Content.WriteAsync(Encoding.UTF8.GetBytes("new"));
                await write.CommitAsync(CancellationToken.None);
            }

            Assert.AreEqual(Octal("640"), (await _session.GetEntryAsync(file, CancellationToken.None))!.Mode);
        }
        finally
        {
            await _session.DeleteAsync(file, recursive: false, CancellationToken.None);
        }
    }

    [TestMethod]
    [CoversNode("io-locations-transfer-permissions")]
    public async Task ACopyOnTheDistro_IsMadeWithItsSourcesBits_AndOneOverAFileKeepsThatFiles()
    {
        var name   = Guid.NewGuid().ToString("N");
        var source = $"{WslUnderTest.Base}/src-{name}";
        var made   = $"{WslUnderTest.Base}/made-{name}";
        var over   = $"{WslUnderTest.Base}/over-{name}";
        await WslUnderTest.RunInDistroAsync(_distro.Name, "sh", "-c",
            $"mkdir -p {WslUnderTest.Base} && printf src > {source} && chmod 700 {source} && printf old > {over} && chmod 644 {over}");
        try
        {
            await _session.CopyWithinAsync(source, made, replace: false, CancellationToken.None);
            await _session.CopyWithinAsync(source, over, replace: true, CancellationToken.None);

            Assert.AreEqual(Octal("700"), (await _session.GetEntryAsync(made, CancellationToken.None))!.Mode);
            Assert.AreEqual(Octal("644"), (await _session.GetEntryAsync(over, CancellationToken.None))!.Mode);
        }
        finally
        {
            foreach (var path in new[] { source, made, over })
                await _session.DeleteAsync(path, recursive: false, CancellationToken.None);
        }
    }
}
