using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Provisioning;

namespace Nexaflow.AiHost.Store;

/// <summary>What of the files the catalog names are in the store (<see cref="DescribeModelStore"/>).</summary>
internal sealed class DescribeModelStoreHandler(HostModelStore store) : HostRequestHandler<DescribeModelStore>
{
    protected override Task HandleAsync(DescribeModelStore request, Func<HostReply, Task> reply, CancellationToken ct)
        => reply(new ModelStoreDescribed(request.RequestId, store.Describe()));
}

/// <summary>Fetches files the catalog names into the store (<see cref="FetchModelFiles"/>), saying how far it has got as bytes come.</summary>
internal sealed class FetchModelFilesHandler(HostModelStore store) : HostRequestHandler<FetchModelFiles>
{
    protected override async Task HandleAsync(FetchModelFiles request, Func<HostReply, Task> reply, CancellationToken ct)
    {
        var told = new Told(p => reply(new Progress(request.RequestId, FetchModelFiles.Fetching, p.Received, p.Total)));
        try
        {
            await store.FetchAsync(request.Paths, told, ct);
        }
        catch (ModelRoomException ex)
        {
            await told.DrainAsync();
            await reply(new NoRoomForModelFiles(request.RequestId, ex.Room.Bytes, ex.Room.Free, ex.Room.Drive));
            throw new HostRequestException(HostErrorKind.Refused, ex.Message);
        }
        catch (ModelDownloadsOffException ex)
        {
            throw new HostRequestException(HostErrorKind.Refused, ex.Message);
        }
        catch (InvalidDataException ex)
        {
            throw new HostRequestException(HostErrorKind.Invalid, ex.Message);
        }
        await told.DrainAsync();
    }

    /// <summary>Each step reported as a reply, in the order reported, the next sent only once the last is: a download reports from
    /// the thread it reads on, many times a second.</summary>
    private sealed class Told(Func<AssetProgress, Task> send) : IProgress<AssetProgress>
    {
        private readonly Lock _gate = new();
        private Task _last = Task.CompletedTask;

        public void Report(AssetProgress value)
        {
            lock (_gate) _last = _last.ContinueWith(_ => send(value), TaskScheduler.Default).Unwrap();
        }

        public Task DrainAsync()
        {
            lock (_gate) return _last;
        }
    }
}

/// <summary>Removes files the catalog names from the store (<see cref="RemoveModelFiles"/>).</summary>
internal sealed class RemoveModelFilesHandler(HostModelStore store) : HostRequestHandler<RemoveModelFiles>
{
    protected override Task HandleAsync(RemoveModelFiles request, Func<HostReply, Task> reply, CancellationToken ct)
        => store.RemoveAsync(request.Paths, ct);
}
