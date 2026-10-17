using System.Net;
using System.Net.Sockets;

namespace Nexaflow.Tests.Providers.Support;

/// <summary>
/// A loopback port where nothing speaks HTTP: every connection is accepted and reset at once, so a provider pointed
/// here fails at the socket as it would against a backend that is down — in milliseconds. A closed port is not the
/// same on Windows: the stack retries a refused loopback connect for about two seconds, and a client that retries
/// (OpenAI's makes four attempts) pays that each time.
/// </summary>
internal sealed class DeadBackend : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);

    public DeadBackend()
    {
        _listener.Start();
        _ = ResetEveryConnectionAsync();
    }

    /// <summary>The backend's base address, with no trailing slash.</summary>
    public string Url => $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}";

    private async Task ResetEveryConnectionAsync()
    {
        try
        {
            while (true)
            {
                using var socket = await _listener.AcceptSocketAsync();
                socket.LingerState = new LingerOption(true, 0);   // closed with a reset, not a graceful FIN
            }
        }
        catch (SocketException) { }          // the listener was stopped
        catch (ObjectDisposedException) { }
    }

    public void Dispose() => _listener.Stop();
}
