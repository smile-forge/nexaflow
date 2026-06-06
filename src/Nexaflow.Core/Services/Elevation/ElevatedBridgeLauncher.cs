using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Elevation.Contracts;

namespace Nexaflow.Core.Services.Elevation;

/// <summary>
/// Server side of the privilege bridge. For each request it opens a private message-mode named pipe,
/// launches the bridge exe elevated (UAC), authenticates the connection with a one-time token, exchanges
/// the request/result, and tears the bridge down. The host stays non-elevated; only the short-lived
/// bridge is ever elevated. Off-UI-thread safe — does not touch any window.
/// </summary>
public sealed class ElevatedBridgeLauncher
{
    private const int ErrorCancelled = 1223; // ERROR_CANCELLED — user declined the UAC prompt.

    private readonly string _bridgePath;

    /// <param name="bridgePath">Override the bridge exe location (tests point this at a stub).</param>
    public ElevatedBridgeLauncher(string? bridgePath = null) =>
        _bridgePath = bridgePath ?? Path.Combine(
            AppContext.BaseDirectory, "PrivilegeBridge", "Nexaflow.PrivilegeBridge.exe");

    public async Task<ElevatedResult> RunAsync(ElevatedRequest request, CancellationToken ct = default)
    {
        if (request is null || request.Operations.Count == 0)
            return ElevatedResult.Fail(ElevatedErrorKind.Unexpected, "No operations to run.");

        if (!File.Exists(_bridgePath))
            return ElevatedResult.Fail(ElevatedErrorKind.BridgeNotFound,
                $"Privilege bridge not found at {_bridgePath}.");

        var pipeName = "nexaflow-elevation-" + Guid.NewGuid().ToString("N");
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

        NamedPipeServerStream? server = null;
        Process? bridge = null;
        try
        {
            server = new NamedPipeServerStream(
                pipeName, PipeDirection.InOut, maxNumberOfServerInstances: 1,
                PipeTransmissionMode.Message, PipeOptions.Asynchronous);

            // runas requires UseShellExecute=true → no stdio redirect; the result returns over the pipe.
            // Only the (non-sensitive) pipe name + one-time token go on the command line.
            try
            {
                bridge = Process.Start(new ProcessStartInfo(_bridgePath, $"{pipeName} {token}")
                {
                    UseShellExecute = true,
                    Verb            = "runas",
                    WindowStyle     = ProcessWindowStyle.Hidden,
                });
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
            {
                return ElevatedResult.Declined();
            }
            catch (Win32Exception ex)
            {
                return ElevatedResult.Fail(ElevatedErrorKind.BridgeLaunchFailed,
                    $"Failed to launch the elevated helper: {ex.Message}");
            }

            if (bridge is null)
                return ElevatedResult.Fail(ElevatedErrorKind.BridgeLaunchFailed, "Elevated helper did not start.");

            // Wait for the bridge to connect — bounded so a never-arriving helper can't hang the caller.
            using (var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                connectCts.CancelAfter(BridgeProtocol.ConnectTimeoutMs);
                try
                {
                    await server.WaitForConnectionAsync(connectCts.Token);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return ElevatedResult.Fail(ElevatedErrorKind.Cancelled, "The elevated request was cancelled.");
                }
                catch (OperationCanceledException)
                {
                    return ElevatedResult.Fail(ElevatedErrorKind.Timeout,
                        "The elevated helper did not connect in time.");
                }
            }

            // Read in message frames so large requests/results (e.g. a long PATH) are never truncated.
            server.ReadMode = PipeTransmissionMode.Message;

            using var ioCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            ioCts.CancelAfter(BridgeProtocol.IoTimeoutMs);

            // Handshake: the bridge must echo the one-time token, defeating pipe-name squatting.
            var hello = await PipeFraming.ReadAsync<BridgeHello>(server, ioCts.Token);
            if (hello is null || !TokensMatch(hello.Token, token))
                return ElevatedResult.Fail(ElevatedErrorKind.HandshakeFailed,
                    "The elevated helper failed authentication.");

            await PipeFraming.WriteAsync(server, request, ioCts.Token);
            var result = await PipeFraming.ReadAsync<ElevatedResult>(server, ioCts.Token);
            return result ?? ElevatedResult.Fail(ElevatedErrorKind.Unexpected,
                "The elevated helper returned no result.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return ElevatedResult.Fail(ElevatedErrorKind.Cancelled, "The elevated request was cancelled.");
        }
        catch (Exception ex)
        {
            return ElevatedResult.Fail(ElevatedErrorKind.Unexpected, ex.Message);
        }
        finally
        {
            server?.Dispose();
            TryKill(bridge);
        }
    }

    private static bool TokensMatch(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));

    private static void TryKill(Process? p)
    {
        try { if (p is { HasExited: false }) p.Kill(entireProcessTree: true); }
        catch { /* already gone */ }
        p?.Dispose();
    }
}
