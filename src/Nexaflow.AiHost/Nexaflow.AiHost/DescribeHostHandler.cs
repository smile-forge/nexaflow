using Nexaflow.AiHost.Contracts;

namespace Nexaflow.AiHost;

internal sealed class DescribeHostHandler(ExecutionRuntime runtime) : HostRequestHandler<DescribeHost>
{
    protected override Task HandleAsync(DescribeHost request, Func<HostReply, Task> reply, CancellationToken ct)
        => reply(new HostDescription(request.RequestId, runtime.Version, runtime.ExecutionProviders, NativeRuntime.Pack, runtime.GpusNow(), runtime.Memory));
}
