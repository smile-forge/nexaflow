using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Nexaflow.Tests.Providers.Support;

/// <summary>
/// A loopback HTTP server that answers each GET path it was given with that JSON, and any other with 404 — a backend
/// whose answers are known, so what a provider asks of it and makes of the answers can be read back without the
/// network. Each request is kept: its path, and its Authorization header.
/// </summary>
internal sealed class CannedBackend : IDisposable
{
    private readonly TcpListener                         _listener = new(IPAddress.Loopback, 0);
    private readonly IReadOnlyDictionary<string, string> _answers;

    public CannedBackend(IReadOnlyDictionary<string, string> answers)
    {
        _answers = answers;
        _listener.Start();
        _ = AnswerEveryConnectionAsync();
    }

    /// <summary>The backend's base address, with no trailing slash.</summary>
    public string Url => $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}";

    /// <summary>Every request asked of it so far, in the order they came.</summary>
    public ConcurrentQueue<(string Path, string? Authorization)> Requests { get; } = new();

    private async Task AnswerEveryConnectionAsync()
    {
        try
        {
            while (true) _ = AnswerAsync(await _listener.AcceptSocketAsync());
        }
        catch (SocketException) { }          // the listener was stopped
        catch (ObjectDisposedException) { }
    }

    /// <summary>One request, one answer, and the connection closed — so the client never sends another down it.</summary>
    private async Task AnswerAsync(Socket socket)
    {
        using var owned = socket;
        try
        {
            await using var stream = new NetworkStream(socket);
            using var       reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);

            var     path          = (await reader.ReadLineAsync())?.Split(' ') is [_, var target, ..] ? target : "";
            string? authorization = null;
            for (var header = await reader.ReadLineAsync(); !string.IsNullOrEmpty(header); header = await reader.ReadLineAsync())
                if (header.StartsWith("Authorization:", StringComparison.OrdinalIgnoreCase))
                    authorization = header["Authorization:".Length..].Trim();
            Requests.Enqueue((path, authorization));

            var (status, body) = _answers.TryGetValue(path, out var json) ? ("200 OK", json) : ("404 Not Found", """{"error":"not found"}""");
            var content = Encoding.UTF8.GetBytes(body);
            await stream.WriteAsync(Encoding.ASCII.GetBytes(
                $"HTTP/1.1 {status}\r\nContent-Type: application/json\r\nContent-Length: {content.Length}\r\nConnection: close\r\n\r\n"));
            await stream.WriteAsync(content);
        }
        catch (IOException) { }              // the client went away mid-answer
    }

    public void Dispose() => _listener.Stop();
}
