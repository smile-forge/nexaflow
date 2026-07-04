using Nexaflow.Providers.Aria;

namespace Nexaflow.Tests.Providers.Aria;

/// <summary>
/// Lifecycle guards on <see cref="AriaNamedPipeClient"/> that hold without a live pipe server:
/// send-before-connect, use-after-dispose, and idempotent disposal.
/// </summary>
[TestClass]
public class AriaNamedPipeClientTests
{
    [TestMethod]
    public void DefaultPipeName_IsAriaComms()
    {
        string name = AriaNamedPipeClient.DefaultPipeName;   // local avoids const-vs-literal analyzer noise
        Assert.AreEqual("aria-comms", name);
    }

    [TestMethod]
    public async Task SendAsync_BeforeConnect_Throws()
    {
        await using var client = new AriaNamedPipeClient();
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => client.SendAsync(new SimpleMessage { Text = "x" }));
    }

    [TestMethod]
    public async Task SendAsync_AfterDispose_ThrowsObjectDisposed()
    {
        var client = new AriaNamedPipeClient();
        await client.DisposeAsync();
        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(
            () => client.SendAsync(new SimpleMessage { Text = "x" }));
    }

    [TestMethod]
    public async Task DisposeAsync_IsIdempotent()
    {
        var client = new AriaNamedPipeClient();
        await client.DisposeAsync();
        await client.DisposeAsync();   // must not throw
    }
}
