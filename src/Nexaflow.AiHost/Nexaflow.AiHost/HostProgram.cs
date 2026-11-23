using System.Diagnostics;
using System.IO.Pipes;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Generation;
using Nexaflow.AiHost.Drawing;
using Nexaflow.AiHost.Models;
using Nexaflow.AiHost.Speech;

using Nexaflow.AiHost.Filming;
using Nexaflow.AiHost.Restoring;
using Nexaflow.AiHost.Selecting;
using Nexaflow.AiHost.Reading;

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
        if (pipeName is null) return 2;

        using var log = new HostLog(Argument(args, AiHostProtocol.LogDirArgument));
        try
        {
            string token = (await Console.In.ReadLineAsync())?.Trim() ?? "";

            var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync((int)AiHostProtocol.ConnectTimeout.TotalMilliseconds);
            // Sections open to this host's own account: a private host's app runs as the same user.
            HostChannel? opened = null;
            using var sections = OperatingSystem.IsWindows()
                ? new HostSections(System.Security.Principal.WindowsIdentity.GetCurrent().User!, TimeProvider.System, message => opened!.SendAsync(message))
                : null;
            await using var channel = opened = new HostChannel(pipe, sections);

            await channel.SendAsync(new Hello(token, AiHostProtocol.Version, Environment.ProcessId));
            if (await channel.ReceiveAsync() is not Welcome)
            {
                log.Write("the app did not accept this host; exiting");
                return 3;
            }

            log.Write($"connected (process {Environment.ProcessId}, protocol {AiHostProtocol.Version})");
            // The store is the host's alone; its runtime is the CUDA pack from it, where this machine's cards and driver run the pack.
            var store = new Store.HostModelStore(Argument(args, AiHostProtocol.StoreArgument) ?? Store.HostModelStore.DefaultRoot,
                                                 Arguments(args, AiHostProtocol.AdoptArgument), Catalog.MachineCards.Probe(),
                                                 downloadsAllowed: !args.Contains(AiHostProtocol.NoDownloadsArgument));
            store.Changed += (_, path) => _ = TellAsync(channel, new ModelFilesChanged([path]));
            NativeRuntime.Load(store.RuntimePack());
            int memoryShare = int.TryParse(Argument(args, AiHostProtocol.MemoryShareArgument), System.Globalization.CultureInfo.InvariantCulture, out var share)
                ? share : AiHostProtocol.DefaultMemoryShare;
            using var runtime = new ExecutionRuntime(log, memoryShare);
            using var models  = new ModelRegistry(runtime, ModelAdapters.Default, ModelRunners.Default, log, store, runtime.Memory);
            var scheduler = new GpuScheduler();
            var server = new HostServer(channel, log,
            [
                new DescribeHostHandler(runtime),
                new Store.DescribeModelStoreHandler(store),
                new Store.FetchModelFilesHandler(store),
                new Store.RemoveModelFilesHandler(store),
                new LoadModelHandler(models),
                new UnloadModelHandler(models),
                new GenerateHandler(models, scheduler),
                new ListenHandler(models, scheduler),
                new SpeakHandler(models, scheduler, new SpeechPhonemizers()),
                new DrawHandler(models, [new ZImageDrawing(models, scheduler), new HiDreamDrawing(models, scheduler), new MossDrawing(models, scheduler), new KleinDrawing(models, scheduler),
                                                         new QwenEditDrawing(models, scheduler)]),
                new FilmHandler(models, [new WanFilming(models, scheduler)]),
                new RestoreHandler(models, scheduler),
                new SelectHandler(models, scheduler),
                new FindTextHandler(models),
                new Erasing.EraseHandler(models, scheduler),
                new Checking.CheckPictureHandler(models),
            new SceneDepth.SceneDepthHandler(models, scheduler),
            ], sections: sections);
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

    /// <summary>Every value given for <paramref name="name"/>, in order.</summary>
    private static IReadOnlyList<string> Arguments(string[] args, string name)
        => [.. args.Select((a, i) => (a, i)).Where(p => p.a == name && p.i + 1 < args.Length).Select(p => args[p.i + 1])];

    /// <summary>Tells the app something unasked; an app already gone hears nothing.</summary>
    private static async Task TellAsync(HostChannel channel, HostMessage message)
    {
        try { await channel.SendAsync(message); }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException) { }
    }
}
