using Nexaflow.IO.Location.Wsl.Protocol;

namespace Nexaflow.WslHelper;

/// <summary>
/// The helper's stdout, which only frames reach. Requests answer side by side, so every frame is written whole under one
/// gate and flushed; a request cancelled while it waits for the gate writes nothing.
/// </summary>
internal sealed class ReplyChannel(Stream output)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task SendAsync(WslWriter reply, CancellationToken ct)
    {
        byte[] frame = reply.ToFrame();
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ct.ThrowIfCancellationRequested();
            Write(frame);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Sends from a thread of the helper's own, which may block while the backend is slow to read.</summary>
    public void Send(WslWriter reply)
    {
        byte[] frame = reply.ToFrame();
        _gate.Wait();
        try
        {
            Write(frame);
        }
        finally
        {
            _gate.Release();
        }
    }

    // Never cut short: a frame written in part would have the backend read the next one from its middle.
    private void Write(byte[] frame)
    {
        output.Write(frame);
        output.Flush();
    }
}
