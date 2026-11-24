using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Service;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.AiHost;

/// <summary>
/// The service's supervisor with real workers — the host executable, started as the service starts it (<see cref="ServiceUnderTest"/>):
/// a client taken on the service's pipe is served in another process, welcomed as the supervisor's; a worker that dies is replaced and
/// the client joins the next; and stopping the service ends its worker as asked, not killed. An app joining it starts no host of its
/// own, so a join that failed would fail its request.
/// </summary>
[TestClass]
[CoversNode("ai-host")]
public class RealServiceTests
{
    private static readonly TimeSpan Generous = TimeSpan.FromSeconds(60);

    private static async Task<HostDescription> DescribeAsync(AiHostClient client)
    {
        HostDescription? description = null;
        await foreach (var reply in client.SendAsync(id => new DescribeHost(id), Generous)) description = reply as HostDescription;
        return description ?? throw new AssertFailedException("the host did not describe itself");
    }

    [TestMethod]
    public async Task AClientOfTheService_IsServedByAWorkerInAProcessOfItsOwn()
    {
        await using var service = ServiceUnderTest.Start();
        await using var client = service.Join("Test");

        var description = await DescribeAsync(client);

        StringAssert.StartsWith(description.RuntimeVersion, "1.");
        Assert.AreNotEqual(Environment.ProcessId, service.Workers.Single().Id, "served in the worker, welcomed as the service");
    }

    [TestMethod]
    public async Task AWorkerThatDies_IsReplaced_AndTheAppJoinsTheNext()
    {
        await using var service = ServiceUnderTest.Start();
        await using var client = service.Join("Test");
        await DescribeAsync(client);

        service.Workers.Single().Kill();

        // The request that finds the connection gone fails; the next joins the service again, which starts a worker for it.
        HostDescription? next = null;
        for (int attempt = 0; attempt < 3 && next is null; attempt++)
        {
            try { next = await DescribeAsync(client); }
            catch (AiHostException ex) when (ex.Failure == AiHostFailure.Lost) { }
        }
        Assert.IsNotNull(next);
        Assert.AreEqual(2, service.Workers.Count);
    }

    [TestMethod]
    public async Task StoppingTheService_EndsItsWorker_AsAsked()
    {
        var service = ServiceUnderTest.Start();
        await using (var client = service.Join("Test"))
            await DescribeAsync(client);
        var worker = service.Workers.Single();

        await service.DisposeAsync();

        Assert.AreEqual(WorkerExit.Stopped, await worker.Exited.WaitAsync(Generous));
    }
}
