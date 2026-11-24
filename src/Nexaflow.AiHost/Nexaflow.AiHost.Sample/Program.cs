using Nexaflow.AiHost.Catalog;
using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Provisioning;

// An app that is not Nexaflow, running a language model of the AI host's catalog through the SDK alone. It joins the machine's AI
// host service where one runs — sharing its models and its store with Nexaflow and every other app on the machine — and otherwise
// starts a private host of its own. It asks the host for the model's files — the host fetches them into its store, which it alone
// keeps — and, on the machine's first NVIDIA card where the CUDA pack suits it, for that too; then it holds the model's chat part —
// the host placing it beside whatever else it holds, for whichever app — asks one question at background priority, as an app
// thinking for itself would, writes the reply as it comes, and lets go.
//
//   Nexaflow.AiHost.Sample [--host <Nexaflow.AiHost.exe>] [--model <catalog id>] [--store <folder>] [--prompt <text>]
//
// --host is the host it starts where no service runs; --store keeps that host to a store of its own, and never joins the service,
// whose store is the machine's.

var options = new Dictionary<string, string>(StringComparer.Ordinal);
for (int i = 0; i + 1 < args.Length; i += 2) options[args[i]] = args[i + 1];
string modelId = options.GetValueOrDefault("--model") ?? "gemma-4-e4b-it";
string? hostPath = options.GetValueOrDefault("--host");
string? ownStore = options.GetValueOrDefault("--store");
IAiHostService? service = ownStore is null ? AiHostService.Machine : null;
if ((hostPath is null && service?.RunningProcessId() is null)
    || HostCatalog.Models.FirstOrDefault(m => m.Id == modelId && m.Kind == HostedKind.Language) is not { } entry)
{
    Console.Error.WriteLine("usage: Nexaflow.AiHost.Sample [--host <Nexaflow.AiHost.exe>] [--model <a language model of the catalog>] [--store <folder>] [--prompt <text>]");
    Console.Error.WriteLine("       --host is needed where the machine runs no AI host service, or with --store.");
    return 2;
}
string prompt = options.GetValueOrDefault("--prompt") ?? "Name the three primary colours of light, in one line.";

var machine = MachineCards.Probe();
bool onGpu = CudaRuntimePack.Applies(machine) && entry.RunsOn(HostCatalog.Cuda);
string runtime = onGpu ? HostCatalog.Cuda : HostCatalog.Cpu;

await using var host = new AiHostClient(new ProcessAiHostLauncher(hostPath is null ? "" : Path.GetFullPath(hostPath), ownStore),
                                        app: "Nexaflow.AiHost.Sample", service: service);
var store = new AiHostModelStore(host);
await store.RefreshAsync();
IReadOnlyList<ModelAsset> files = [.. entry.FilesFor(runtime), .. onGpu ? CudaRuntimePack.Files : []];
if (files.Any(f => !store.Has(f)))
{
    Console.Error.WriteLine($"fetching {store.FetchBytes(files) >> 20} MiB into the host's store");
    await store.EnsureAllAsync(files, new Progress<AssetProgress>(p => Console.Error.Write($"\r{p.Fraction:P0}")));
    Console.Error.WriteLine();
}
host.Environment = onGpu ? AiHostEnvironment.OnCuda : AiHostEnvironment.Cpu;

var model = new ModelRef(entry.Id, entry.Part(HostedRoles.Chat, runtime).Name);
await using var hold = await new HostHolds(host).HoldAsync([model], RequestPriority.Background, new Progress<string>(stage => Console.Error.WriteLine(stage)));
if (hold.PlacementOf(model) is not { } placed)
{
    Console.Error.WriteLine($"{entry.DisplayName} could not be held: {hold.WhyUnplaced(model)}");
    return 1;
}
Console.Error.WriteLine(placed.Device is { } device
    ? $"running {entry.DisplayName} on GPU {device}, with {placed.ContextTokens:N0} tokens of context"
    : $"running {entry.DisplayName} on the CPU, with {placed.ContextTokens:N0} tokens of context");

int written = 0;
await foreach (var reply in host.SendAsync(id => new Generate(id, model, [new ChatTurn(TurnRole.User, prompt)], 128, RequestPriority.Background),
                                           TimeSpan.FromMinutes(5)))
{
    if (reply is not TextDelta text) continue;
    Console.Write(text.Text);
    written += text.Text.Length;
}
Console.WriteLine();
return written > 0 ? 0 : 1;
