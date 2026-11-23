namespace Nexaflow.AiHost.Catalog;

/// <summary>
/// What placing a catalog entry's parts takes (<see cref="PlacementNeed"/>), worked out from the catalog alone: the host plans where
/// they run with it, and an app predicts from it what this machine would run before any host has (<see cref="ModelPlacement.Expected"/>).
/// </summary>
public static class PlacementNeeds
{
    /// <summary>The host's name for a part: its entry's id, and the part's name in it — the same for each of its builds.</summary>
    public static string PartId(HostedEntry entry, HostedPart part) => $"{entry.Id}/{part.Name}";

    /// <summary>The parts of <paramref name="entry"/> held with it on <paramref name="runtime"/>: all but those it loads on demand.</summary>
    public static IEnumerable<HostedPart> Held(HostedEntry entry, string runtime) => entry.PartsFor(runtime).Where(p => !p.LoadsOnDemand);

    /// <summary>What placing each of <paramref name="entry"/>'s parts held with it on <paramref name="runtime"/> takes.</summary>
    public static IReadOnlyList<PlacementNeed> Of(HostedEntry entry, string runtime) => [.. Held(entry, runtime).Select(p => Of(entry, p))];

    /// <summary>
    /// What placing <paramref name="part"/> takes: how it runs on a GPU and on the CPU — the same part where it runs on either, its CPU
    /// build where it has one — what its context costs, held in 8 bits (<see cref="ContextCache.Q8"/>), and what it takes to read in
    /// longer steps, to be cut between a GPU and the CPU, or to run lighter (<see cref="Lighter"/>).
    /// </summary>
    public static PlacementNeed Of(HostedEntry entry, HostedPart part)
    {
        var onCpu = part.ExecutionProvider is null ? part
                  : entry.Parts.FirstOrDefault(p => p.Name == part.Name && p.ExecutionProvider == HostCatalog.Cpu);
        bool language = part.BytesPerToken > 0;
        return new PlacementNeed(Unplaced(entry, part), onCpu is null ? null : Unplaced(entry, onCpu) with { MemoryBytes = FilesBytes(entry, onCpu) },
                                 language ? ContextCache.Q8.BytesPerToken(part.BytesPerToken) : 0, language ? HostCatalog.ShortestContext : 0,
                                 part.Layers, language ? WeightsBytes(entry, part) : 0, part.BatchBytesPerToken,
                                 ContextCache.Q8.BytesPerToken(part.BytesPerSequence), part.BackgroundBytes, part.StateBytes, part.ExpertBytes,
                                 Lighter(entry, part));
    }

    /// <summary>How <paramref name="part"/> runs holding less on a GPU, most first: streamed, then decoded in tiles.</summary>
    public static IReadOnlyList<ModelSpec> Lighter(HostedEntry entry, HostedPart part)
    {
        var builds = new List<ModelSpec>();
        if (part.Streamed is { } streamed)
            builds.Add(Unplaced(entry, part) with { File = streamed.File, MemoryBytes = streamed.MemoryBytes, HostBytes = streamed.HostBytes });
        if (part.Tiled is { } tiled)
            builds.Add(Unplaced(entry, part) with { TileCells = tiled.Cells, MemoryBytes = tiled.MemoryBytes, HostBytes = tiled.HostBytes });
        return builds;
    }

    /// <summary>The files of <paramref name="part"/>'s build: those in its folder.</summary>
    public static IReadOnlyList<Provisioning.ModelAsset> FilesOf(HostedEntry entry, HostedPart part)
        => [.. entry.Files.Where(f => f.RelativePath.StartsWith(part.Folder + "/", StringComparison.Ordinal))];

    /// <summary>What <paramref name="part"/>'s own file holds — a language model's weights, its GGUF — read whole.</summary>
    private static long WeightsBytes(HostedEntry entry, HostedPart part)
        => entry.Files.FirstOrDefault(f => f.RelativePath == $"{part.Folder}/{part.File}")?.Size ?? 0;

    /// <summary>What <paramref name="part"/> holds in memory on the CPU, with no context: its files, read whole — the catalog's
    /// footprints are its GPU's.</summary>
    private static long FilesBytes(HostedEntry entry, HostedPart part) => FilesOf(entry, part).Sum(f => f.Size);

    /// <summary>A part as the catalog describes it, placed nowhere yet: its folder the store's name for it.</summary>
    private static ModelSpec Unplaced(HostedEntry entry, HostedPart part)
        => new(PartId(entry, part), part.Runner, part.Folder, part.File, part.Adapter, part.MemoryBytes, part.ContextTokens, part.CudaOptions,
               part.Projector, ContextCache: part.BytesPerToken > 0 ? ContextCache.Q8 : ContextCache.F16);
}
