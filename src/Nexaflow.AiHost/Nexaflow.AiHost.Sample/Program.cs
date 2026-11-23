using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Contracts;

// An app that is not Nexaflow, running its own AI host through the SDK alone. It starts a private host, loads a GGUF on the
// machine's first NVIDIA card where the CUDA pack suits it — else on the CPU — asks one question at background priority, as
// an app thinking for itself would, and writes the reply as it comes.
//
//   Nexaflow.AiHost.Sample --host <Nexaflow.AiHost.exe> --gguf <model.gguf> [--pack <CUDA pack folder>] [--prompt <text>]

var options = new Dictionary<string, string>(StringComparer.Ordinal);
for (int i = 0; i + 1 < args.Length; i += 2) options[args[i]] = args[i + 1];
if (!options.TryGetValue("--host", out var hostPath) || !options.TryGetValue("--gguf", out var gguf))
{
    Console.Error.WriteLine("usage: Nexaflow.AiHost.Sample --host <Nexaflow.AiHost.exe> --gguf <model.gguf> [--pack <CUDA pack>] [--prompt <text>]");
    return 2;
}
string prompt = options.GetValueOrDefault("--prompt") ?? "Name the three primary colours of light, in one line.";

var machine = MachineCards.Probe();
bool onGpu = options.TryGetValue("--pack", out var pack) && CudaRuntimePack.Applies(machine);
var work = Directory.CreateTempSubdirectory("aihost-sample-");

await using var host = new AiHostClient(new ProcessAiHostLauncher(Path.GetFullPath(hostPath), work.FullName))
{
    Environment = onGpu ? new AiHostEnvironment(pack) : AiHostEnvironment.Cpu,
};
gguf = Path.GetFullPath(gguf);
var model = new ModelSpec("sample/chat", "llama-chat", Path.GetDirectoryName(gguf)!, Path.GetFileName(gguf), null,
                          new FileInfo(gguf).Length + (2L << 30), 4096, Device: onGpu ? 0 : null);
Console.Error.WriteLine(onGpu ? $"running on {machine.Cards[0].Name}" : "running on the CPU");

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
