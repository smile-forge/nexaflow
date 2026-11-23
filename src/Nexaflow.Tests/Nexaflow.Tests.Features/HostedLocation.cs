using System.Collections.Concurrent;
using System.Text;
using Nexaflow.IO.Common;
using Nexaflow.IO.Common.Locations;
using Nexaflow.IO.Location.Memory;

namespace Nexaflow.Tests.Features;

/// <summary>
/// An in-memory location whose host runs programs, mounted in a runtime's file system through the session pool as a
/// real location is: what a feature that runs its tools on a location's host is tested against. Programs are answered by
/// <see cref="Answer"/>, keyed by the arguments they were given; every run is kept in <see cref="Runs"/>.
/// </summary>
internal sealed class HostedLocation : IAsyncDisposable
{
    public const string Root = "::memory-box";

    private sealed class NoSecrets : ISecretStore
    {
        public string? Read(string target) => null;
        public void Write(string target, string? userName, string secret) { }
        public bool Delete(string target) => false;
    }

    private readonly ConcurrentDictionary<string, Func<ProgramRun, ProgramExit>> _answers = new(StringComparer.Ordinal);
    private readonly LocationSessionPool _pool;
    private readonly LocationSet _set;

    public HostedLocation()
    {
        Host = new MemoryLocationHost("box", TimeSpan.Zero) { Programs = Run };
        var registry = new LocationProviderRegistry();
        registry.Register(new MemoryLocationProvider(Host));
        _pool = new LocationSessionPool(registry, new StoredCredentialSource(new NoSecrets()));
        _set  = _pool.Acquire([new ConnectionProfile("memory-box", "memory", "Box", "box")]);
        Files = new LocationFileSystem(VirtualFileSystem.Instance);
        Files.RegisterLocation(_set.Mounts[0]);
    }

    public MemoryLocationHost Host { get; }

    public LocationFileSystem Files { get; }

    public IEnumerable<ProgramRun> Runs => Host.Runs;

    /// <summary>This file system's path of a host path: <c>/src/wt</c> → <c>::memory-box\src\wt</c>.</summary>
    public static string PathOf(string hostPath)
        => hostPath == "/" ? Root : Root + "\\" + string.Join('\\', hostPath.Trim('/').Split('/').Select(LocationPath.EncodeSegment));

    /// <summary>Answers a program run with these arguments, wherever it runs.</summary>
    public void Answer(string program, string arguments, Func<ProgramRun, ProgramExit> answer) => _answers[$"{program} {arguments}"] = answer;

    public void Answer(string program, string arguments, int exitCode, string output = "", string errors = "")
        => Answer(program, arguments, _ => new ProgramExit(exitCode, output, errors));

    /// <summary>A folder and every folder above it.</summary>
    public void Folder(string path)
        => Host.With(nodes =>
        {
            for (var at = path; at.Length > 1; at = at[..Math.Max(at.LastIndexOf('/'), 1)])
                nodes.TryAdd(at, new MemoryLocationHost.Node(IsDirectory: true, [], DateTime.Now));
            return 0;
        });

    public void File(string path, string text)
    {
        Folder(path[..path.LastIndexOf('/')]);
        Host.With(nodes => nodes[path] = new MemoryLocationHost.Node(IsDirectory: false, Encoding.UTF8.GetBytes(text), DateTime.Now));
    }

    public bool Exists(string path) => Host.With(nodes => nodes.ContainsKey(path));

    public IReadOnlyList<string> Paths() => Host.With(nodes => nodes.Keys.ToList());

    private ProgramExit Run(ProgramRun run)
        => _answers.TryGetValue($"{run.Program} {string.Join(' ', run.Arguments)}", out var answer)
            ? answer(run)
            : throw new AssertFailedException($"Nothing answers '{run.Program} {string.Join(' ', run.Arguments)}'.");

    public async ValueTask DisposeAsync()
    {
        _pool.Release(_set);
        await _pool.DisposeAsync();
    }
}
