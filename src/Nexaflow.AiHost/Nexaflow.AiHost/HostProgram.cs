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
/// The host's life: take the token from standard input, make the store, the runtime, the models and the handlers, open the pipe
/// under the name it was given (<see cref="HostListener"/>), serve every client that connects with the token, and exit once the
/// last has gone. Exit codes are for the log only — the app watches its connection.
/// </summary>
internal static class HostProgram
{
    public static async Task<int> RunAsync(string[] args)
    {
        string? pipeName = Argument(args, AiHostProtocol.PipeArgument);
        if (pipeName is null || !OperatingSystem.IsWindows()) return 2;

        HostPolicy policy;
        try
        {
            policy = HostPolicy.Read(Argument(args, AiHostProtocol.PolicyArgument) ?? Path.Combine(HostPolicy.DefaultFolder, HostPolicy.FileName));
        }
        catch (InvalidDataException ex)
        {
            await Console.Error.WriteLineAsync(ex.Message);
            return 4;
        }
        using var log = new HostLog(Argument(args, AiHostProtocol.LogDirArgument) ?? policy.LogFolder);
        try
        {
            string token = (await Console.In.ReadLineAsync())?.Trim() ?? "";
            log.Write($"started (process {Environment.ProcessId}, protocol {AiHostProtocol.Version})");
            // The store is the host's alone; its runtime is the CUDA pack from it, where this machine's cards and driver run the pack.
            var store = new Store.HostModelStore(Argument(args, AiHostProtocol.StoreArgument) ?? Store.HostModelStore.DefaultRoot,
                                                 Arguments(args, AiHostProtocol.AdoptArgument), Catalog.MachineCards.Probe(),
                                                 downloadsAllowed: !args.Contains(AiHostProtocol.NoDownloadsArgument));
            NativeRuntime.Load(store.RuntimePack());
            int memoryShare = int.TryParse(Argument(args, AiHostProtocol.MemoryShareArgument), System.Globalization.CultureInfo.InvariantCulture, out var share)
                ? share : policy.MemoryShare ?? AiHostProtocol.DefaultMemoryShare;
            using var runtime = new ExecutionRuntime(log, memoryShare);
            using var models  = new ModelRegistry(runtime, ModelRunners.Default, log, store, runtime.Memory, policy.IdleUnload);
            using var held    = new HeldModels(models, store, NativeRuntime.Pack is null ? Catalog.HostCatalog.Cpu : Catalog.HostCatalog.Cuda,
                                               runtime.GpusNow, runtime.Memory, log);
            var requests  = new RequestModels(held);
            var scheduler = new GpuScheduler();
            IHostRequestHandler[] handlers =
            [
                new DescribeHostHandler(runtime, held),
                new Store.DescribeModelStoreHandler(store),
                new Store.FetchModelFilesHandler(store),
                new Store.RemoveModelFilesHandler(store),
                new HoldModelsHandler(held),
                new GenerateHandler(models, held, scheduler),
                new ForgetConversationHandler(models),
                new ListenHandler(models, requests, scheduler),
                new SpeakHandler(models, requests, scheduler, new SpeechPhonemizers()),
                new DrawHandler(models, requests, [new ZImageDrawing(models, scheduler), new HiDreamDrawing(models, scheduler), new MossDrawing(models, scheduler),
                                                   new KleinDrawing(models, scheduler), new QwenEditDrawing(models, scheduler)]),
                new FilmHandler(models, requests, [new WanFilming(models, scheduler)]),
                new RestoreHandler(held, scheduler),
                new SelectHandler(held, scheduler),
                new FindTextHandler(held),
                new Erasing.EraseHandler(held, scheduler),
                new Checking.CheckPictureHandler(held),
                new SceneDepth.SceneDepthHandler(held, scheduler),
            ];

            // Every client hears of the store's files as they come and go, whoever fetched them.
            var listener = new HostListener(pipeName, token, handlers, policy, log, endsWithLastClient: true);
            store.Changed += (_, path) => listener.Tell(new ModelFilesChanged([path]));
            await listener.RunAsync();
            log.Write("the last client has gone; exiting");
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
}
