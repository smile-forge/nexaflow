using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Contracts;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.AiHost;

/// <summary>The host executable itself, as the app runs it: started, describing its runtime, and gone when the app
/// lets go of it.</summary>
[TestClass]
[CoversNode("ai-host")]
public class RealHostTests
{
    private string _aiDir = null!;

    [TestInitialize]
    public void Init() => _aiDir = Directory.CreateTempSubdirectory("nexaflow-aihost-").FullName;

    [TestCleanup]
    public void Done()
    {
        try { Directory.Delete(_aiDir, recursive: true); }
        catch (IOException) { /* a log still closing */ }
    }

    private static string HostPath => Path.Combine(AppContext.BaseDirectory, "AiHost", "Nexaflow.AiHost.exe");

    /// <summary>Starts the real host, keeping hold of the process the client watches.</summary>
    private sealed class Recording(IAiHostLauncher inner) : IAiHostLauncher
    {
        public IAiHostProcess? Process { get; private set; }

        public IAiHostProcess Start(string pipeName, string token) => Process = inner.Start(pipeName, token);
    }

    [TestMethod]
    public async Task TheHostDescribesItsRuntime()
    {
        await using var client = new AiHostClient(new ProcessAiHostLauncher(HostPath, _aiDir));

        HostDescription? description = null;
        await foreach (var reply in client.SendAsync(id => new DescribeHost(id), TimeSpan.FromSeconds(30)))
            description = reply as HostDescription;

        Assert.IsNotNull(description);
        StringAssert.StartsWith(description.RuntimeVersion, "1.");
        CollectionAssert.Contains(description.ExecutionProviders.ToArray(), "CPUExecutionProvider");
        Assert.IsTrue(Directory.EnumerateFiles(Path.Combine(_aiDir, "logs"), "aihost-*.log").Any(), "the host keeps a log");
    }

    [TestMethod]
    public async Task TheHostExits_WhenTheAppLetsGo()
    {
        var launcher = new Recording(new ProcessAiHostLauncher(HostPath, _aiDir));
        var client = new AiHostClient(launcher);
        await foreach (var _ in client.SendAsync(id => new DescribeHost(id), TimeSpan.FromSeconds(30))) { }
        var exited = launcher.Process!.Exited;

        await client.DisposeAsync();

        Assert.AreEqual(0, await exited.WaitAsync(TimeSpan.FromSeconds(10)), "closing the pipe ends the host cleanly");
    }

    [TestMethod]
    public async Task AMissingHost_IsUnavailable()
    {
        await using var client = new AiHostClient(new ProcessAiHostLauncher(Path.Combine(_aiDir, "absent.exe"), _aiDir));

        var ex = await Assert.ThrowsExactlyAsync<AiHostException>(async () =>
        {
            await foreach (var _ in client.SendAsync(id => new DescribeHost(id), TimeSpan.FromSeconds(30))) { }
        });

        Assert.AreEqual(AiHostFailure.Unavailable, ex.Failure);
    }
}
