using System.Diagnostics;
using Nexaflow.AiHost.Contracts;

namespace Nexaflow.AiHost.Models;

/// <summary>Loads a model into the resident set.</summary>
internal sealed class LoadModelHandler(ModelRegistry models) : HostRequestHandler<LoadModel>
{
    protected override async Task HandleAsync(LoadModel request, Func<HostReply, Task> reply, CancellationToken ct)
    {
        var started = Stopwatch.GetTimestamp();
        using var lease = await models.AcquireAsync(request.Model, resident: true,
            stage => reply(new Progress(request.RequestId, stage, 0, 0)), ct);
        await reply(new ModelLoaded(request.RequestId, request.Model.Device, Stopwatch.GetElapsedTime(started).TotalSeconds));
    }
}

/// <summary>Takes a model out of the resident set.</summary>
internal sealed class UnloadModelHandler(ModelRegistry models) : HostRequestHandler<UnloadModel>
{
    protected override Task HandleAsync(UnloadModel request, Func<HostReply, Task> reply, CancellationToken ct)
    {
        models.Release(request.ModelId);
        return Task.CompletedTask;
    }
}
