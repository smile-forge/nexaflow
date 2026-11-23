using Nexaflow.AiHost.Catalog;
using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Provisioning;

// An app that is not Nexaflow, running a language model of the AI host's catalog through the SDK alone. It starts a private host,
// asks it for the model's files — the host fetches them into its store, which it alone keeps — and, on the machine's first NVIDIA
// card where the CUDA pack suits it, for that too; then it asks one question at background priority, as an app thinking for itself
// would, and writes the reply as it comes.
//
//   Nexaflow.AiHost.Sample --host <Nexaflow.AiHost.exe> [--model <catalog id>] [--store <folder>] [--prompt <text>]

var options = new Dictionary<string, string>(StringComparer.Ordinal);
for (int i = 0; i + 1 < args.Length; i += 2) options[args[i]] = args[i + 1];
string modelId = options.GetValueOrDefault("--model") ?? "gemma-4-e4b-it";
if (!options.TryGetValue("--host", out var hostPath)
    || HostCatalog.Models.FirstOrDefault(m => m.Id == modelId && m.Kind == HostedKind.Language) is not { } entry)
{
    Console.Error.WriteLine("usage: Nexaflow.AiHost.Sample --host <Nexaflow.AiHost.exe> [--model <a language model of the catalog>] [--store <folder>] [--prompt <text>]");
    return 2;
}
string prompt = options.GetValueOrDefault("--prompt") ?? "Name the three primary colours of light, in one line.";

var machine = MachineCards.Probe();
bool onGpu = CudaRuntimePack.Applies(machine) && entry.RunsOn(HostCatalog.Cuda);
string runtime = onGpu ? HostCatalog.Cuda : HostCatalog.Cpu;

await using var host = new AiHostClient(new ProcessAiHostLauncher(Path.GetFullPath(hostPath), options.GetValueOrDefault("--store")));
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

const int context = 4096;
var part = entry.Part(HostedRoles.Chat, runtime);
var model = new ModelSpec(entry.Id, part.Runner, part.Folder, part.File, part.Adapter, part.MemoryBytes + part.BytesPerToken * context, context,
                          Device: onGpu ? 0 : null, Projector: part.Projector);
Console.Error.WriteLine(onGpu ? $"running {entry.DisplayName} on {machine.Cards[0].Name}" : $"running {entry.DisplayName} on the CPU");

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
