using Nexaflow.AiHost.Contracts;

namespace Nexaflow.AiHost;

internal sealed class DescribeHostHandler(ExecutionRuntime runtime, Models.HeldModels held, Store.HostModelStore store) : HostRequestHandler<DescribeHost>
{
    protected override Task HandleAsync(DescribeHost request, Func<HostReply, Task> reply, CancellationToken ct)
    {
        var (placed, unplaced) = held.Describe();
        return reply(new HostDescription(request.RequestId, runtime.Version, runtime.ExecutionProviders,
                                         NativeRuntime.Pack is { } pack ? Path.GetFileName(pack) : null, runtime.GpusNow(), runtime.Memory, placed, unplaced,
                                         store.DescribeMachine()));
    }
}
