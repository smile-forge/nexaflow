using System.IO.Pipes;
using System.Net.Mime;
using System.Security.Principal;
using System.Text.Json;

namespace Nexaflow.Providers.Aria
{
    /// <summary>
    /// Maintains a persistent named-pipe connection to the Aria service.
    /// <para>
    /// Call <see cref="ConnectAsync"/> once to establish the connection, then use
    /// <see cref="SendAsync"/> to post messages at any time.  All incoming frames from
    /// Aria — whether direct replies or Aria-initiated messages — are surfaced via the
    /// <see cref="MessageReceived"/> and <see cref="IsTyping"/> events.
    /// </para>
    /// </summary>
    public class AriaNamedPipeClient : IAsyncDisposable
    {
        // ── Constants ──────────────────────────────────────────────────────────

        /// <summary>The well-known name of the Aria named pipe.</summary>
        public const string DefaultPipeName = "aria-comms";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        // ── Configuration ──────────────────────────────────────────────────────

        private readonly string _pipeName;
        private readonly string _serverName;

        // ── State ──────────────────────────────────────────────────────────────

        private NamedPipeClientStream? _pipe;
        private Task? _receiveLoop;
        private CancellationTokenSource? _cts;
        private bool _disposed;

        // Windows identity captured at connect time; stamped on every outbound frame
        private string _windowsUserId = string.Empty;

        // Serialises concurrent sends so frames never interleave on the wire
        private readonly SemaphoreSlim _writeLock = new(1, 1);

        // ── Events ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Raised when Aria sends a message. This may be a reply to something the user
        /// sent, or a message Aria initiates independently.
        /// </summary>
        public event EventHandler<SimpleMessage>? MessageReceived;

        /// <summary>
        /// Raised when Aria's typing state changes.
        /// <c>true</c> = Aria started composing; <c>false</c> = Aria finished composing.
        /// </summary>
        public event EventHandler<bool>? IsTyping;

        /// <summary>Raised when the pipe connection to the server is lost.</summary>
        public event EventHandler? Disconnected;

        // ── Constructor ────────────────────────────────────────────────────────

        /// <param name="pipeName">Pipe name to connect to (defaults to <see cref="DefaultPipeName"/>).</param>
        /// <param name="serverName">Host running the pipe server (defaults to the local machine).</param>
        public AriaNamedPipeClient(string? pipeName = null, string? serverName = null)
        {
            _pipeName = pipeName ?? DefaultPipeName;
            _serverName = serverName ?? ".";
        }

        // ── Public API ─────────────────────────────────────────────────────────

        /// <summary>
        /// Connects to the Aria pipe server and starts the background receive loop.
        /// Must be called once before <see cref="SendAsync"/>.
        /// </summary>
        /// <exception cref="InvalidOperationException">Already connected.</exception>
        public async Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_pipe is not null)
                throw new InvalidOperationException("Already connected. Dispose and recreate to reconnect.");

            // Stamp frames with the Windows account name only — a stable, non-PII identifier.
            // (This used to be upgraded to the user's e-mail via an AccountManagement lookup;
            // dropped: it put PII on every frame and pulled in a directory-services dependency.)
            WindowsIdentity identity = WindowsIdentity.GetCurrent();
            if (identity == null)
            {
                return;
            }
            string accountName = identity.Name; // e.g. "DOMAIN\\jones"
            string[] parts = accountName.Split('\\');
            _windowsUserId = parts.Length > 1 ? parts[1] : parts[0];

            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            _pipe = new NamedPipeClientStream(
                _serverName, _pipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);

            await _pipe.ConnectAsync(_cts.Token);
            _pipe.ReadMode = PipeTransmissionMode.Message;

            _receiveLoop = Task.Run(() => ReceiveLoopAsync(_cts.Token), CancellationToken.None);
        }

        /// <summary>
        /// Sends <paramref name="message"/> to the Aria service and returns immediately.
        /// Any response will arrive asynchronously via <see cref="MessageReceived"/>.
        /// </summary>
        public async Task SendAsync(SimpleMessage message, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_pipe is null || !_pipe.IsConnected)
                throw new InvalidOperationException("Not connected. Call ConnectAsync first.");

            var frame = new PipeFrame
            {
                Type = PipeFrameType.UserMessage,
                WindowsUserId = _windowsUserId,
                Payload = message
            };

            await WriteFrameAsync(frame, cancellationToken);
        }

        // ── Receive loop ───────────────────────────────────────────────────────

        private async Task ReceiveLoopAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested && _pipe is { IsConnected: true })
                {
                    PipeFrame frame;
                    try
                    {
                        frame = await ReadFrameAsync(ct);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch
                    {
                        break; // pipe broken
                    }

                    switch (frame.Type)
                    {
                        case PipeFrameType.AssistantMessage when frame.Payload is not null:
                            MessageReceived?.Invoke(this, frame.Payload);
                            break;

                        case PipeFrameType.TypingStarted:
                            IsTyping?.Invoke(this, true);
                            break;

                        case PipeFrameType.TypingStopped:
                            IsTyping?.Invoke(this, false);
                            break;
                    }
                }
            }
            finally
            {
                Disconnected?.Invoke(this, EventArgs.Empty);
            }
        }

        // ── Wire helpers ───────────────────────────────────────────────────────

        private async Task WriteFrameAsync(PipeFrame frame, CancellationToken ct)
        {
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(frame, JsonOptions);

            await _writeLock.WaitAsync(ct);
            try
            {
                await _pipe!.WriteAsync(bytes, ct);
                await _pipe!.FlushAsync(ct);
            }
            finally
            {
                _writeLock.Release();
            }
        }

        private async Task<PipeFrame> ReadFrameAsync(CancellationToken ct)
        {
            using var ms = new MemoryStream();
            byte[] buffer = new byte[4096];

            do
            {
                int read = await _pipe!.ReadAsync(buffer, ct);
                if (read == 0)
                    throw new InvalidOperationException("Pipe closed before a complete frame was received.");
                ms.Write(buffer, 0, read);
            }
            while (!_pipe!.IsMessageComplete);

            return JsonSerializer.Deserialize<PipeFrame>(ms.ToArray(), JsonOptions)
                ?? throw new InvalidOperationException("Received a null or unreadable frame from the server.");
        }

        // ── IAsyncDisposable ───────────────────────────────────────────────────

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;

            _cts?.Cancel();

            if (_receiveLoop is not null)
            {
                try { await _receiveLoop; }
                catch { /* swallow cancellation */ }
            }

            _pipe?.Dispose();
            _cts?.Dispose();
            _writeLock.Dispose();

            GC.SuppressFinalize(this);
        }
    }
}
