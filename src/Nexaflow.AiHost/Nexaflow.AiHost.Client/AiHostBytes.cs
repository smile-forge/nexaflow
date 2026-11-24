using Nexaflow.AiHost.Contracts;

namespace Nexaflow.AiHost.Client;

/// <summary>Blocks of bytes made ready to send to the AI host.</summary>
public static class AiHostBytes
{
    /// <summary>
    /// <paramref name="bytes"/> copied into bytes the host can read in place (<see cref="IAiHost.AllocateAsync"/>): a section it made
    /// when they are large, else an array that crosses in the request's frame. Send them in one request, and dispose them after.
    /// </summary>
    /// <exception cref="AiHostException">The host refused the room, or the connection was lost while it was asked for.</exception>
    public static async Task<SharedBytes> ShareAsync(this IAiHost host, ReadOnlyMemory<byte> bytes, CancellationToken ct = default)
    {
        var shared = await host.AllocateAsync(bytes.Length, ct).ConfigureAwait(false);
        bytes.Span.CopyTo(shared.Span);
        return shared;
    }

    /// <summary>The file at <paramref name="path"/> read straight into bytes the host can read in place, as <see cref="ShareAsync"/>
    /// makes them.</summary>
    /// <exception cref="AiHostException">The host refused the room, or the connection was lost while it was asked for.</exception>
    /// <exception cref="IOException">The file could not be read, or is past what one block holds.</exception>
    public static async Task<SharedBytes> ShareFileAsync(this IAiHost host, string path, CancellationToken ct = default)
    {
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (file.Length > Array.MaxLength) throw new IOException($"{path} is {file.Length >> 20} MiB, past what one block sent to the AI host holds.");
        var shared = await host.AllocateAsync((int)file.Length, ct).ConfigureAwait(false);
        try
        {
            await file.ReadExactlyAsync(shared.Memory, ct).ConfigureAwait(false);
            return shared;
        }
        catch
        {
            shared.Dispose();
            throw;
        }
    }

    /// <summary>
    /// <paramref name="turns"/> ready to send: each medium past <see cref="SharedBytes.InlineLimit"/> moved into a section the host
    /// made, the rest left to cross in the request's frame. Make them for each request sent — a section belongs to the connection it
    /// was made on — and dispose them once it has ended.
    /// </summary>
    /// <exception cref="AiHostException">The host refused the room, or the connection was lost while it was asked for.</exception>
    public static async Task<SharedTurns> ShareMediaAsync(this IAiHost host, IReadOnlyList<ChatTurn> turns, CancellationToken ct = default)
    {
        if (!turns.Any(t => t.Media?.Any(m => m.Bytes.Length > SharedBytes.InlineLimit) == true)) return new SharedTurns(turns, []);
        var sections = new List<SharedBytes>();
        try
        {
            var shared = new List<ChatTurn>(turns.Count);
            foreach (var turn in turns)
            {
                if (turn.Media?.Any(m => m.Bytes.Length > SharedBytes.InlineLimit) != true)
                {
                    shared.Add(turn);
                    continue;
                }
                var media = new List<ChatMedia>(turn.Media.Count);
                foreach (var medium in turn.Media)
                {
                    if (medium.Bytes.Length <= SharedBytes.InlineLimit)
                    {
                        media.Add(medium);
                        continue;
                    }
                    var section = await host.ShareAsync(medium.Bytes.Memory, ct).ConfigureAwait(false);
                    sections.Add(section);
                    media.Add(medium with { Bytes = section });
                }
                shared.Add(turn with { Media = media });
            }
            return new SharedTurns(shared, sections);
        }
        catch
        {
            foreach (var section in sections) section.Dispose();
            throw;
        }
    }
}

/// <summary>Turns ready to send (<see cref="AiHostBytes.ShareMediaAsync"/>), and the sections their large media were moved into,
/// closed when this is disposed.</summary>
public sealed class SharedTurns(IReadOnlyList<ChatTurn> turns, IReadOnlyList<SharedBytes> sections) : IDisposable
{
    public IReadOnlyList<ChatTurn> Turns => turns;

    public void Dispose()
    {
        foreach (var section in sections) section.Dispose();
    }
}
