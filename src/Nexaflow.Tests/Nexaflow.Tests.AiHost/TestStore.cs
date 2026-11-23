using Nexaflow.AiHost.Catalog;
using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Provisioning;

namespace Nexaflow.Tests.AiHost;

/// <summary>
/// The model store the tests that run real models read: <c>NEXAFLOW_TEST_STORE</c> names one laid out as the AI host keeps it —
/// the machine's own, <c>%ProgramData%\Smile-Forge\Models</c>, or a copy — holding each model under its catalog folder and, for a
/// GPU, the CUDA pack. A test's host keeps that store and downloads nothing; a request names a model as an app does, by its
/// folder in the store (<see cref="Spec"/>). A build not yet in the catalog is tried by putting it under the folder the catalog
/// will name. Unset, every such test is inconclusive.
/// </summary>
internal static class TestStore
{
    public const string Variable = "NEXAFLOW_TEST_STORE";

    /// <summary>The store's folder; null where the variable is not set.</summary>
    public static string? Root => Environment.GetEnvironmentVariable(Variable) is { Length: > 0 } root ? root : null;

    public static string HostPath => Path.Combine(AppContext.BaseDirectory, "AiHost", "Nexaflow.AiHost.exe");

    /// <summary>
    /// A host keeping the test store — on the CUDA pack where the store holds it and this machine runs it, which <paramref name="gpu"/>
    /// asks for: a test of a GPU part is inconclusive without it. Null where no store is named.
    /// </summary>
    public static async Task<AiHostClient?> HostAsync(bool gpu = true)
    {
        if (!gpu) return Root is { } root ? Launch(root) : null;
        var client = await HostOnEitherAsync();
        if (client is { Environment.Cuda: true }) return client;
        if (client is not null) await client.DisposeAsync();
        return null;
    }

    /// <summary>
    /// A host keeping the test store, on the CUDA pack where the store holds it and this machine runs it and on the CPU otherwise — for a
    /// test of parts that run on either; its <see cref="AiHostClient.Environment"/> says which. Null where no store is named.
    /// </summary>
    public static async Task<AiHostClient?> HostOnEitherAsync()
    {
        if (Root is not { } root) return null;
        var client = Launch(root);
        if (CudaRuntimePack.Applies(MachineCards.Probe()))
        {
            var store = new AiHostModelStore(client);
            await store.RefreshAsync();
            if (CudaRuntimePack.IsIn(store)) client.Environment = AiHostEnvironment.OnCuda;
        }
        return client;
    }

    private static AiHostClient Launch(string root) => new(new ProcessAiHostLauncher(HostPath, store: root, downloads: false));

    /// <summary>What a test says when <see cref="HostAsync"/> gave it no host.</summary>
    public static string Missing(bool gpu = true)
        => gpu ? $"{Variable} names no model store holding the CUDA pack this machine runs." : $"{Variable} names no model store.";

    /// <summary>
    /// <paramref name="model"/>'s part <paramref name="part"/> — its name in the model — as a request names it: its folder in the store,
    /// its file and runner from the catalog, on <paramref name="device"/> (null for the CPU, which takes the part's CPU build where
    /// it has one), holding <paramref name="memoryBytes"/> where the test says, else what the catalog says it holds.
    /// </summary>
    public static ModelSpec Spec(string model, string part, int? device, long? memoryBytes = null, int contextTokens = 0)
    {
        var entry = HostCatalog.Find(model) ?? throw new ArgumentException($"The catalog has no model '{model}'.", nameof(model));
        var runtime = device is null ? HostCatalog.Cpu : HostCatalog.Cuda;
        var built = entry.PartsFor(runtime).FirstOrDefault(p => p.Name == part) ?? entry.Parts.FirstOrDefault(p => p.Name == part)
                    ?? throw new ArgumentException($"'{model}' has no part '{part}'.", nameof(part));
        return new ModelSpec($"{model}/{part}", built.Runner, built.Folder, built.File, built.Adapter, memoryBytes ?? built.MemoryBytes, contextTokens,
                             built.CudaOptions, built.Projector, device);
    }

    /// <summary>
    /// The listening models of <paramref name="model"/> — a speech-to-text model of the catalog — as a request names them: the recogniser
    /// and the transcriber on the first GPU where <paramref name="cuda"/>, holding what the catalog says, and on the CPU otherwise; the
    /// voice activity and turn detectors on the CPU either way.
    /// </summary>
    public static ListenModels ListenModelsOf(string model, bool cuda)
    {
        int? device = cuda ? 0 : null;
        long? held = cuda ? null : 0;
        return new ListenModels(Spec(model, HostedRoles.VoiceActivity, null, 0), Spec(model, HostedRoles.Recognizer, device, held),
                                Spec(model, HostedRoles.Transcriber, device, held), Spec(model, HostedRoles.TurnDetector, null, 0));
    }

    /// <summary>Where <paramref name="relative"/> lies in the test store — for a test that reads a model's file itself, as a reference
    /// check does; the host never takes a path.</summary>
    public static string PathOf(string relative) => Path.Combine(Root ?? throw new InvalidOperationException(Missing(false)), relative);

    /// <summary>
    /// The voice of <paramref name="model"/> that reads <paramref name="language"/>, as a request names it: the part that speaks it as
    /// <see cref="Spec"/> gives it, the voice's file and — for a language read from a lexicon — the lexicon's, both in that part's folder.
    /// </summary>
    public static VoiceSpec Voice(string model, string language, int? device, long? memoryBytes = null)
    {
        var entry = HostCatalog.Find(model) as HostedModel ?? throw new ArgumentException($"The catalog has no model '{model}'.", nameof(model));
        var voice = entry.Voices.FirstOrDefault(v => v.Language == language)
                    ?? throw new ArgumentException($"'{model}' has no voice for '{language}'.", nameof(language));
        return new VoiceSpec(language, Spec(model, voice.Part, device, memoryBytes), voice.File, voice.Lexicon);
    }

    /// <summary>
    /// <see cref="Spec"/> for a runner loaded in the test's own process rather than in a host: its folder the part's path in the test
    /// store, as the host resolves it. Inconclusive where no store is named.
    /// </summary>
    public static ModelSpec InProcessSpec(string model, string part, int? device, long? memoryBytes = null)
    {
        if (Root is null) Assert.Inconclusive(Missing(gpu: false));
        var spec = Spec(model, part, device, memoryBytes);
        return spec with { Folder = PathOf(spec.Folder) };
    }

    /// <summary>
    /// Where <paramref name="model"/>'s part <paramref name="part"/> lies in the test store — its build for the GPU, or with
    /// <paramref name="gpu"/> false its CPU build where it has one — for a test that reads its files in its own process, as a
    /// tokenizer's check does. Inconclusive where no store is named.
    /// </summary>
    public static string PartFolder(string model, string part, bool gpu = true) => InProcessSpec(model, part, gpu ? 0 : null).Folder;

    /// <summary>
    /// The CUDA pack's folder in the test store, where the store holds the pack whole and this machine runs it — for a test that binds its
    /// own process to it, as the host binds itself (<see cref="PackRuntime"/>); null otherwise.
    /// </summary>
    public static string? RuntimePackFolder
        => Root is { } root && CudaRuntimePack.Applies(MachineCards.Probe()) ? CudaRuntimePack.FolderIn(Files(root)) : null;

    /// <summary>Whether the test store holds every one of <paramref name="files"/>, verified — for a test whose app fetches what its store
    /// lacks, which nothing may do to the test store.</summary>
    public static bool HoldsAll(IEnumerable<ModelAsset> files) => Root is { } root && files.All(Files(root).Has);

    /// <summary>The test store read in this process, as the host reads it, never fetching.</summary>
    private static ModelAssetStore Files(string root) => new(root, [], downloadsAllowed: false);
}
