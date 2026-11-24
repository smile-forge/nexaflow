using System.Globalization;
using System.Security.Principal;
using System.ServiceProcess;
using Nexaflow.AiHost.Catalog;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Service;
using Nexaflow.AiHost.Store;

namespace Nexaflow.AiHost;

/// <summary>
/// The host's life, in each of the ways it runs. Started by an app for itself (<see cref="AiHostProtocol.PipeArgument"/>), it takes
/// the token from standard input, opens the pipe under the name it was given, serves every client that connects with the token,
/// and exits once the last has gone. As the machine's service (<see cref="ServiceArgument"/>) it is a supervisor that takes clients
/// on the service's pipe and hands them to its worker (<see cref="WorkerArgument"/>), which serves them. The installer runs it once
/// more to give the store to the service, or back to the users (<see cref="SecureStoreArgument"/>). Exit codes are for the log
/// only — an app watches its connection — save a worker's, which its supervisor reads (<see cref="WorkerExit"/>).
/// </summary>
internal static class HostProgram
{
    /// <summary>Run as the Windows service — the service manager's command line.</summary>
    public const string ServiceArgument = "--service";

    /// <summary>Run as the service's worker: the broker, serving the clients its supervisor hands it on standard input.</summary>
    public const string WorkerArgument = "--worker";

    /// <summary>The process a worker answers for — its supervisor, the one the service manager names — which its welcome names.</summary>
    public const string ServeAsArgument = "--serve-as";

    /// <summary>Run by the installer, as SYSTEM: <c>service</c> moves the installing user's earlier models in
    /// (<see cref="AiHostProtocol.AdoptArgument"/>) and gives the store and the host's folder to the service; <c>users</c> gives the
    /// store back to every user, as the service goes.</summary>
    public const string SecureStoreArgument = "--secure-store";

    public static async Task<int> RunAsync(string[] args)
    {
        if (!OperatingSystem.IsWindows()) return 2;
        if (args.Contains(ServiceArgument)) return RunService(args);
        if (Argument(args, SecureStoreArgument) is { } access) return await SecureStoreAsync(access, args);

        HostPolicy policy;
        try
        {
            policy = ReadPolicy(args);
        }
        catch (InvalidDataException ex)
        {
            await Console.Error.WriteLineAsync(ex.Message);
            return 4;
        }
        if (args.Contains(WorkerArgument)) return await RunWorkerAsync(args, policy);
        return Argument(args, AiHostProtocol.PipeArgument) is { } pipeName ? await RunPrivateAsync(pipeName, args, policy) : 2;
    }

    private static HostPolicy ReadPolicy(string[] args)
        => HostPolicy.Read(Argument(args, AiHostProtocol.PolicyArgument) ?? Path.Combine(HostPolicy.DefaultFolder, HostPolicy.FileName));

    /// <summary>A host an app started for itself.</summary>
    private static async Task<int> RunPrivateAsync(string pipeName, string[] args, HostPolicy policy)
    {
        using var log = new HostLog(Argument(args, AiHostProtocol.LogDirArgument) ?? policy.LogFolder);
        try
        {
            string token = (await Console.In.ReadLineAsync())?.Trim() ?? "";
            log.Write($"started (process {Environment.ProcessId}, protocol {AiHostProtocol.Version})");
            string? root = Argument(args, AiHostProtocol.StoreArgument);
            // The machine's store, made by the first host that needs it, is every user's while no service keeps it.
            if (root is null && !Directory.Exists(HostModelStore.DefaultRoot)) StoreAccess.ForUsers(HostModelStore.DefaultRoot, takeOwnership: false);
            var store = new HostModelStore(root ?? HostModelStore.DefaultRoot, Arguments(args, AiHostProtocol.AdoptArgument), MachineCards.Probe(),
                                           downloadsAllowed: !args.Contains(AiHostProtocol.NoDownloadsArgument));
            int memoryShare = int.TryParse(Argument(args, AiHostProtocol.MemoryShareArgument), CultureInfo.InvariantCulture, out var share)
                ? share : policy.MemoryShare ?? AiHostProtocol.DefaultMemoryShare;
            using var broker = new HostBroker(store, memoryShare, policy, log);

            var listener = new HostListener(new OwnPipes(pipeName), token, broker.Handlers, policy, log, endsWithLastClient: true, memory: broker.Memory);
            // Every client hears of the store's files as they come and go, whoever fetched them.
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

    /// <summary>The service's worker: it serves the clients its supervisor hands it until the supervisor closes its standard input,
    /// or until another runtime is in the store and nothing is held or running, when it exits for its supervisor to start another.</summary>
    private static async Task<int> RunWorkerAsync(string[] args, HostPolicy policy)
    {
        using var log = new HostLog(policy.LogFolder ?? HostPolicy.ServiceLogFolder);
        try
        {
            if (!int.TryParse(Argument(args, ServeAsArgument), NumberStyles.None, CultureInfo.InvariantCulture, out int supervisor)) return 2;
            log.Write($"worker started (process {Environment.ProcessId}, for the service's process {supervisor}, protocol {AiHostProtocol.Version})");
            var store = new HostModelStore(Argument(args, AiHostProtocol.StoreArgument) ?? HostModelStore.DefaultRoot, [], MachineCards.Probe());
            using var broker = new HostBroker(store, policy.MemoryShare ?? AiHostProtocol.DefaultMemoryShare, policy, log);

            using var control = new StreamReader(Console.OpenStandardInput());
            var listener = new HostListener(new HandedPipes(control), token: null, broker.Handlers, policy, log, endsWithLastClient: false,
                                            welcomeAs: supervisor, memory: broker.Memory);
            store.Changed += (_, path) => listener.Tell(new ModelFilesChanged([path]));
            using var stop = new CancellationTokenSource();
            var serving = listener.RunAsync();
            var runtime = new RuntimeWatch(store.RuntimePack, broker.RuntimePack, () => broker.Held.AnyHeld || listener.Busy, RuntimeWatch.Every,
                                           TimeProvider.System).UntilWorthRestartingAsync(stop.Token);
            // A worker no client uses holds a runtime — on the GPU, and in memory — for nobody: it goes, and the next client starts another.
            var idle = new IdleWatch(() => listener.Clients > 0 || broker.Held.AnyHeld, IdleWatch.Linger, IdleWatch.Every, TimeProvider.System)
                .UntilIdleAsync(stop.Token);
            var ended = await Task.WhenAny(serving, runtime, idle);
            if (ended == runtime)
            {
                log.Write("another runtime is in the store, and nothing is held or running: exiting to take it up");
                return WorkerExit.RuntimeChanged;
            }
            if (ended == idle)
            {
                log.Write($"no client for {IdleWatch.Linger.TotalSeconds:0} s, and nothing held: exiting until one comes");
                return WorkerExit.Idle;
            }
            await stop.CancelAsync();
            await serving;
            log.Write("the supervisor asked the worker to stop");
            return WorkerExit.Stopped;
        }
        catch (Exception ex)
        {
            log.Write($"fatal: {ex}");
            return 1;
        }
    }

    /// <summary>The service: its supervisor, until the service manager stops it.</summary>
    private static int RunService(string[] args)
    {
        HostPolicy? policy = null;
        string? unreadable = null;
        try
        {
            policy = ReadPolicy(args);
        }
        catch (InvalidDataException ex)
        {
            unreadable = ex.Message;
        }
        using var log = new HostLog(policy?.LogFolder ?? HostPolicy.ServiceLogFolder);
        log.Write($"service started (process {Environment.ProcessId}, protocol {AiHostProtocol.Version})");
        if (unreadable is not null) log.Write($"the policy file cannot be read, and no worker can start until it can: {unreadable}");

        var pipes = new ServicePipes(AiHostProtocol.ServicePipeName, ServicePipes.For(WindowsIdentity.GetCurrent().User!));
        var launcher = new ProcessWorkerLauncher(Environment.ProcessPath!, [.. args.Where(a => a != ServiceArgument)], log);
        var restarts = WorkerRestarts.Default;
        using var service = new HostWindowsService(new WorkerSupervisor(pipes.AcceptAsync, launcher, log, restarts, TimeProvider.System), log, restarts);
        ServiceBase.Run(service);
        return service.ExitCode;
    }

    /// <summary>The installer's step, as SYSTEM: the store and the host's folder given to the service, the installing user's earlier
    /// models moved in first — the service cannot read that user's folders; or the store given back to every user.</summary>
    private static async Task<int> SecureStoreAsync(string access, string[] args)
    {
        string root = HostModelStore.DefaultRoot;
        switch (access)
        {
            case "service":
                var service = (SecurityIdentifier)new NTAccount("NT SERVICE", AiHostProtocol.ServiceName).Translate(typeof(SecurityIdentifier));
                await AdoptAsync(root, [.. Arguments(args, AiHostProtocol.AdoptArgument).Where(Directory.Exists)]);
                StoreAccess.ForService(root, service, takeOwnership: true);
                StoreAccess.ForService(HostPolicy.DefaultFolder, service, takeOwnership: true);
                return 0;
            case "users":
                StoreAccess.ForUsers(root, takeOwnership: true);
                return 0;
            default:
                await Console.Error.WriteLineAsync($"{SecureStoreArgument} takes service or users, not {access}.");
                return 2;
        }
    }

    /// <summary>Moves into the store what an earlier Nexaflow kept in <paramref name="earlier"/>. What cannot be moved stays where it
    /// is, and is fetched again when it is first wanted.</summary>
    private static async Task AdoptAsync(string root, IReadOnlyList<string> earlier)
    {
        if (earlier.Count == 0) return;
        try
        {
            var store = new HostModelStore(root, earlier, MachineCards.Probe(), downloadsAllowed: false);
            await store.AdoptEarlierAsync(CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await Console.Error.WriteLineAsync($"Earlier models were not all moved into the store: {ex.Message}");
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
