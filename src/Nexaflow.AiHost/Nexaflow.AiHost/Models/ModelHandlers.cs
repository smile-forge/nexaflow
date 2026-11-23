using System.Threading.Channels;
using Nexaflow.AiHost.Contracts;

namespace Nexaflow.AiHost.Models;

/// <summary>Holds parts loaded for as long as the request runs (<see cref="HoldModels"/>): until it is cancelled, its input ends, or the
/// app goes.</summary>
internal sealed class HoldModelsHandler(HeldModels held) : IHostRequestHandler
{
    public Type RequestType => typeof(HoldModels);

    public async Task HandleAsync(HostRequest request, Func<HostReply, Task> reply, ChannelReader<RequestInput> input, CancellationToken ct)
    {
        await using var holding = await held.HoldAsync((HoldModels)request, reply, ct);
        await foreach (var _ in input.ReadAllAsync(ct)) { }   // a hold takes no input: what ends it is its end
    }
}
