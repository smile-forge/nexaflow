using System.IO.Pipes;
using Nexaflow.Elevation.Contracts;
using Nexaflow.PrivilegeBridge.Operations;

namespace Nexaflow.PrivilegeBridge;

/// <summary>
/// Drives one elevated session: connect back to the launcher's private pipe, prove identity with the
/// one-time token, read the request, run it, write the result, exit. Exit codes are advisory only — the
/// real outcome travels in the <see cref="ElevatedResult"/> over the pipe.
/// </summary>
internal static class BridgeHost
{
    public static async Task<int> RunAsync(string[] args)
    {
        // args[0] = pipe name, args[1] = one-time token (both non-sensitive; the request travels over the pipe).
        if (args.Length < 2 || string.IsNullOrWhiteSpace(args[0]) || string.IsNullOrWhiteSpace(args[1]))
            return 2;

        var pipeName = args[0];
        var token = args[1];

        try
        {
            using var pipe = new NamedPipeClientStream(
                ".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

            using (var connectCts = new CancellationTokenSource(BridgeProtocol.ConnectTimeoutMs))
                await pipe.ConnectAsync(connectCts.Token);

            pipe.ReadMode = PipeTransmissionMode.Message;

            using var ioCts = new CancellationTokenSource(BridgeProtocol.IoTimeoutMs);

            // 1. Prove we are the process the launcher spawned.
            await PipeFraming.WriteAsync(pipe, new BridgeHello { Token = token }, ioCts.Token);

            // 2. Receive the work.
            var request = await PipeFraming.ReadAsync<ElevatedRequest>(pipe, ioCts.Token);
            if (request is null || request.Operations.Count == 0)
            {
                await PipeFraming.WriteAsync(pipe,
                    ElevatedResult.Fail(ElevatedErrorKind.Unexpected, "Empty request."), ioCts.Token);
                return 1;
            }

            // 3. Execute every operation in order.
            var registry = new OperationRegistry();
            var results = request.Operations.Select(registry.Run).ToList();
            var result = ElevatedResult.FromOperations(results);

            // 4. Report back.
            await PipeFraming.WriteAsync(pipe, result, ioCts.Token);
            return result.Success ? 0 : 1;
        }
        catch
        {
            // The launcher treats a lost pipe / nonzero exit as an Unexpected/Timeout result on its side.
            return 3;
        }
    }
}
