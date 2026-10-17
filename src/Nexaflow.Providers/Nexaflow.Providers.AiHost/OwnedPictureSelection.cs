using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Contracts;
using Nexaflow.IO.Common.Hardware;
using Nexaflow.Providers.Common;

namespace Nexaflow.Providers.AiHost;

/// <summary>
/// Things picked out of pictures in the AI host with SAM 3.1 (<see cref="OwnedToolKind.PictureSelection"/>): by clicks on
/// a thing and off it, a box round it, or everything a phrase names.
/// </summary>
public sealed class OwnedPictureSelection : OwnedToolFront
{
    public OwnedPictureSelection(IBackgroundActivityManager activities, IModelAssetStore assets, IAiHost host, HostModelSetup setup, ComputeHardwareReport hardware)
        : this(new OwnedModelRuntime(activities, assets, host, setup, hardware), OwnedModels.Tools.FirstOrDefault(t => t.Kind == OwnedToolKind.PictureSelection))
    {
    }

    internal OwnedPictureSelection(OwnedModelRuntime runtime, OwnedTool? tool) : base(runtime, tool)
    {
    }

    /// <summary>
    /// Opens a selection on the picture in <paramref name="pixels"/> — interleaved 8-bit RGBA, nothing else — and returns
    /// once the host has read it, after which the file may go. The caller holds the session for as long as it selects from
    /// that picture and disposes it after; what the host read goes with it. Never downloads.
    /// </summary>
    /// <exception cref="InvalidOperationException">The tool is not on this machine, or the host could not read the picture.</exception>
    public async Task<OwnedSelectionSession> OpenAsync(string pixels, int width, int height, CancellationToken ct)
    {
        var parts = await PlacedAsync(SelectingParts.All, ct);
        IHostStream stream;
        try
        {
            stream = await Owned.Host.OpenStreamAsync(id => new SelectInPicture(id, parts, pixels, width, height, RequestPriority.Interactive),
                                                      OwnedModelRuntime.StallLimit, ct);
        }
        catch (AiHostException ex)
        {
            throw new InvalidOperationException($"The picture could not be read: {ex.Message}", ex);
        }
        var session = new OwnedSelectionSession(stream, Owned);
        try
        {
            await session.EncodedAsync(ct);
            return session;
        }
        catch
        {
            await session.DisposeAsync();
            throw;
        }
    }
}

/// <summary>
/// A selection on one picture in the AI host: each ask answered from the one reading of it, in the order asked. The
/// host's replies are read as they come, each answer handed to the ask waiting for its turn — an ask given up on is
/// answered all the same, and its answer let go. Disposing it ends the request, and the host lets the picture go.
/// </summary>
public sealed class OwnedSelectionSession : IAsyncDisposable
{
    private readonly IHostStream _stream;
    private readonly OwnedModelRuntime _owned;
    private readonly Lock _gate = new();
    private readonly Dictionary<int, TaskCompletionSource<HostReply>> _waiting = [];
    private readonly TaskCompletionSource _encoded = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<IDisposable> _madeWay = [];
    private readonly Task _reading;
    private Exception? _ended;
    private int _turns, _disposed;

    internal OwnedSelectionSession(IHostStream stream, OwnedModelRuntime owned)
    {
        _stream = stream;
        _owned = owned;
        _reading = Task.Run(ReadAsync);
    }

    /// <summary>Waits for the host to have read the picture.</summary>
    internal Task EncodedAsync(CancellationToken ct) => _encoded.Task.WaitAsync(ct);

    /// <summary>Picks what clicks on it and off it, or a box round it, mean; its mask is written to the target.</summary>
    /// <exception cref="InvalidOperationException">The host could not, or the selection has ended.</exception>
    public async Task<PointsSelected> SelectAsync(IReadOnlyList<SelectionPoint> points, MaskTarget mask, CancellationToken ct)
        => (PointsSelected)await AskAsync(turn => new SelectByPoints(_stream.RequestId, turn, points, mask), ct);

    /// <summary>Finds everything a phrase and its examples name, surest first; their masks are written to the target one
    /// after another.</summary>
    /// <exception cref="InvalidOperationException">The host could not, or the selection has ended.</exception>
    public async Task<PhraseFound> FindAsync(string phrase, IReadOnlyList<SelectionExample> examples, MaskTarget masks, CancellationToken ct)
        => (PhraseFound)await AskAsync(turn => new SelectByPhrase(_stream.RequestId, turn, phrase, examples, masks), ct);

    private async Task<HostReply> AskAsync(Func<int, RequestInput> ask, CancellationToken ct)
    {
        var answer = new TaskCompletionSource<HostReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        int turn;
        lock (_gate)
        {
            if (_ended is not null) throw new InvalidOperationException($"The selection has ended: {_ended.Message}", _ended);
            turn = ++_turns;
            _waiting[turn] = answer;
        }
        try
        {
            await _stream.WriteAsync(ask(turn), ct);
            return await answer.Task.WaitAsync(ct);
        }
        catch (AiHostException ex)
        {
            throw new InvalidOperationException($"The selection stopped: {ex.Message}", ex);
        }
        finally
        {
            lock (_gate) _waiting.Remove(turn);
        }
    }

    private async Task ReadAsync()
    {
        Exception ended = new InvalidOperationException("The AI host ended the selection.");
        try
        {
            await foreach (var reply in _stream.ReadAsync())
            {
                switch (reply)
                {
                    case MadeWay made:
                        lock (_gate) _madeWay.Add(_owned.MakingWay(made.Models));
                        break;
                    case PictureEncoded:
                        GiveBack();
                        _encoded.TrySetResult();
                        break;
                    case PointsSelected selected:
                        Answer(selected.Turn, selected);
                        break;
                    case PhraseFound found:
                        Answer(found.Turn, found);
                        break;
                }
            }
        }
        catch (Exception ex)   // the host gone, the stream ended or disposed: every ask still waiting hears why
        {
            ended = ex;
        }
        List<TaskCompletionSource<HostReply>> waiting;
        lock (_gate)
        {
            _ended = ended;
            waiting = [.. _waiting.Values];
        }
        var failure = new InvalidOperationException($"The selection stopped: {ended.Message}", ended);
        _encoded.TrySetException(failure);
        foreach (var w in waiting) w.TrySetException(failure);
        GiveBack();
    }

    private void Answer(int turn, HostReply reply)
    {
        GiveBack();
        TaskCompletionSource<HostReply>? waiting;
        lock (_gate) _waiting.TryGetValue(turn, out waiting);
        waiting?.TrySetResult(reply);
    }

    /// <summary>The models that made way for an ask, given back once it is answered.</summary>
    private void GiveBack()
    {
        List<IDisposable> back;
        lock (_gate)
        {
            back = [.. _madeWay];
            _madeWay.Clear();
        }
        foreach (var b in back) b.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        try { await _stream.WriteAsync(new InputEnd(_stream.RequestId)); }
        catch (AiHostException) { }   // a host already gone has let the picture go
        await _stream.DisposeAsync();
        await _reading;
    }
}
