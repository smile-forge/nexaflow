using Nexaflow.Providers.Common;

namespace Nexaflow.Providers.Aria;

/// <summary>
/// Wraps <see cref="AriaNamedPipeClient"/> and sends a text message to Aria.
/// </summary>
public class AriaClientService : IAsyncDisposable
{
    // A new client instance must be created for every connection attempt because
    // AriaNamedPipeClient throws "Already connected" once _pipe has been set,
    // even after a cancelled or failed connect.
    private AriaNamedPipeClient? _pipe;
    private bool _connected;

    // How long to wait for the server pipe to appear before giving up.
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(3);

    /// <summary>Parsed result of a call to <see cref="SendAsync"/>.</summary>
    public record AriaResponse(string RawText, FocusTabInstruction? FocusTab);

    /// <summary>
    /// Thrown when the connection to Aria cannot be established or maintained.
    /// <see cref="Exception.InnerException"/> carries the original OS/pipe exception.
    /// </summary>
    public sealed class AriaConnectionException(string message, Exception inner)
        : LlmProviderException(message, inner);

    public async Task EnsureConnectedAsync()
    {
        if (_connected) return;

        // Dispose any previous (broken) client before creating a fresh one.
        if (_pipe is not null)
        {
            await _pipe.DisposeAsync();
            _pipe = null;
        }

        _pipe = new AriaNamedPipeClient();
        try
        {
            using var cts = new CancellationTokenSource(ConnectTimeout);
            await _pipe.ConnectAsync(cts.Token);
            _connected = true;
        }
        catch (Exception ex)
        {
            await _pipe.DisposeAsync();
            _pipe = null;

            var msg = ex is UnauthorizedAccessException or System.Security.SecurityException
                ? $"Access denied connecting to the Aria named pipe. " +
                  $"Run the Aria service as the same user, or grant this account access to " +
                  $"the pipe \"{AriaNamedPipeClient.DefaultPipeName}\"."
                : $"Could not connect to the Aria service pipe " +
                  $"\"{AriaNamedPipeClient.DefaultPipeName}\": {ex.Message}";

            throw new AriaConnectionException(msg, ex);
        }
    }

    /// <summary>
    /// Sends <paramref name="text"/> to Aria and waits for one reply.
    /// Throws <see cref="AriaConnectionException"/> if the pipe cannot be reached.
    /// </summary>
    public async Task<AriaResponse?> SendAsync(
        string text,
        IReadOnlyList<string>? attachments = null,
        CancellationToken ct = default)
    {
        await EnsureConnectedAsync(); // throws AriaConnectionException on failure

        try
        {
            var tcs = new TaskCompletionSource<SimpleMessage>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            void Handler(object? _, SimpleMessage m) => tcs.TrySetResult(m);
            _pipe!.MessageReceived += Handler;

            SimpleMessage? reply;
            try
            {
                var message = new SimpleMessage { Text = text };
                if (attachments is { Count: > 0 })
                    message.AttachmentPaths.AddRange(attachments);
                await _pipe.SendAsync(message, ct);

                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                using var linked  = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
                linked.Token.Register(() => tcs.TrySetCanceled());

                reply = await tcs.Task;
            }
            finally
            {
                _pipe.MessageReceived -= Handler;
            }

            if (reply?.Text is null) return null;
            return ParseResponse(reply.Text);
        }
        catch (AriaConnectionException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _connected = false;
            throw new AriaConnectionException($"Lost connection to Aria: {ex.Message}", ex);
        }
    }

    private static AriaResponse ParseResponse(string raw)
    {
        FocusTabInstruction? focusTab = null;

        foreach (var line in raw.Split('\n'))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("#focustab ", StringComparison.OrdinalIgnoreCase))
                continue;

            var rest     = trimmed["#focustab ".Length..].Trim();
            var spaceIdx = rest.IndexOf(' ');
            var tabName  = spaceIdx < 0 ? rest : rest[..spaceIdx];
            var @params  = spaceIdx < 0 ? string.Empty : rest[(spaceIdx + 1)..].Trim();
            focusTab = new FocusTabInstruction(tabName, @params);
            raw = raw.Replace(line + "\n", string.Empty).Replace(line, string.Empty).Trim();
            break;
        }

        return new AriaResponse(raw, focusTab);
    }

    public async ValueTask DisposeAsync()
    {
        if (_pipe is not null)
            await _pipe.DisposeAsync();
    }
}
