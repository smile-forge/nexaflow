using System.Diagnostics;
using System.IO.Pipes;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Generation;
using Nexaflow.AiHost.Drawing;
using Nexaflow.AiHost.Models;
using Nexaflow.AiHost.Speech;
using Nexaflow.IO.Common.Hardware;
using Nexaflow.AiHost.Filming;
using Nexaflow.AiHost.Restoring;
using Nexaflow.AiHost.Selecting;

namespace Nexaflow.AiHost;

/// <summary>
/// The host's life: connect to the pipe the app opened, prove itself with the token from standard input, serve
/// requests until the app closes the pipe, exit. Exit codes are for the log only — the app watches the pipe.
/// </summary>
internal static class HostProgram
{
    public static async Task<int> RunAsync(string[] args)
    {
        string? pipeName = Argument(args, AiHostProtocol.PipeArgument);
        string? aiDir    = Argument(args, AiHostProtocol.AiDirArgument);
        if (pipeName is null || aiDir is null) return 2;

        using var log = new HostLog(Path.Combine(aiDir, "logs"));
        try
        {
            string token = (await Console.In.ReadLineAsync())?.Trim() ?? "";

            var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync((int)AiHostProtocol.ConnectTimeout.TotalMilliseconds);
            pipe.ReadMode = PipeTransmissionMode.Message;
            await using var channel = new HostChannel(pipe);

            await channel.SendAsync(new Hello(token, AiHostProtocol.Version, Environment.ProcessId));
            if (await channel.ReceiveAsync() is not Welcome)
            {
                log.Write("the app did not accept this host; exiting");
                return 3;
            }

            log.Write($"connected (process {Environment.ProcessId}, protocol {AiHostProtocol.Version})");
            NativeRuntime.Load(Argument(args, AiHostProtocol.RuntimePackArgument));
            using var runtime = new ExecutionRuntime(log);
            using var models  = new ModelRegistry(runtime, ModelAdapters.Default, ModelRunners.Default, log);
            var scheduler = new GpuScheduler();
            var server = new HostServer(channel, log,
            [
                new DescribeHostHandler(runtime),
                new LoadModelHandler(models),
                new UnloadModelHandler(models),
                new GenerateHandler(models, scheduler),
                new ListenHandler(models, scheduler),
                new SpeakHandler(models, scheduler, new SpeechPhonemizers()),
                new DrawHandler(models, [new ZImageDrawing(models, scheduler), new HiDreamDrawing(models, scheduler)]),
                new FilmHandler(models, [new WanFilming(models, scheduler)]),
                new RestoreHandler(models, scheduler),
                new SelectHandler(models, scheduler),
            ]);
            await server.RunAsync();
            log.Write("the app closed the pipe; exiting");
            return 0;
        }
        catch (Exception ex)
        {
            log.Write($"fatal: {ex}");
            return 1;
        }
    }

    private static string? Argument(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
