using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Nexaflow.Elevation.Contracts;

/// <summary>
/// Length-free framing over a message-mode named pipe, shared by the launcher (server) and the bridge
/// (client) so the wire format can never drift. Each value is one pipe message; reads accumulate until
/// <see cref="PipeStream.IsMessageComplete"/>. Mirrors the Aria pipe convention.
/// </summary>
public static class PipeFraming
{
    public static async Task WriteAsync<T>(PipeStream pipe, T value, CancellationToken ct)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(value, ElevationJson.Options);
        await pipe.WriteAsync(bytes, ct);
        await pipe.FlushAsync(ct);
    }

    public static async Task<T?> ReadAsync<T>(PipeStream pipe, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        byte[] buffer = new byte[8192];
        do
        {
            int read = await pipe.ReadAsync(buffer, ct);
            if (read == 0)
                throw new IOException("Pipe closed before a complete frame was received.");
            ms.Write(buffer, 0, read);
        }
        while (!pipe.IsMessageComplete);

        return JsonSerializer.Deserialize<T>(ms.ToArray(), ElevationJson.Options);
    }
}
