using Nexaflow.AiHost.Contracts;

namespace Nexaflow.AiHost.Catalog;

/// <summary>
/// A part of a model as the host runs it: its files, prepared in <see cref="Folder"/> by the adapter its catalog names, the runner
/// that loads them, where it runs and what it costs there. The host makes it from its catalog and its placement of the part
/// (<see cref="ModelPlacement"/>); a request names the part (<see cref="ModelRef"/>), never how it runs.
/// </summary>
/// <param name="Id">Unique among the models the host holds: the catalog id, with its part for a model made of several.</param>
/// <param name="Runner">What loads and runs it — <c>llama-chat</c> for a language model; a family of speech models each has its own.</param>
/// <param name="Folder">Its folder in the host's model store, as the store names it (<c>models/kokoro-82m</c>): where that lies on
/// disk is the host's to know, and a path that leaves the store is refused.</param>
/// <param name="File">The file it is — a language model's GGUF, a single graph; null for a model whose runner reads several files from its folder.</param>
/// <param name="Adapter">What turns the downloaded files into what the runner reads; null when they are used as downloaded.</param>
/// <param name="MemoryBytes">Memory it holds loaded where <see cref="Device"/> places it, with its context — what
/// admission counts: on a GPU against that GPU's budget, on the CPU against the host's share of this machine's
/// memory (<see cref="HostMemory"/>).</param>
/// <param name="ContextTokens">The longest context a conversation may grow to, for a language model; 0 otherwise.</param>
/// <param name="CudaOptions">CUDA options it needs beyond the host's own.</param>
/// <param name="Projector">A language model's multimodal projector, beside its GGUF in <see cref="Folder"/>: what it
/// sees pictures and hears recordings through; null for a model of text alone.</param>
/// <param name="Device">The GPU it runs on, numbered as <see cref="HostGpu.Device"/>; null runs it on the CPU.</param>
/// <param name="BatchTokens">How many tokens a language model reads in one step: a prompt is read in slices this long, and the
/// next token of every reply being written goes with them. Longer steps read a long prompt in fewer, each working in more memory.</param>
/// <param name="ContextCache">How a language model's context holds each token it has read.</param>
/// <param name="GpuLayers">How many of a language model's layers run on <see cref="Device"/>, the rest on the CPU, with the context
/// each layer keeps; null for every one of them.</param>
/// <param name="Sequences">How many sequences a language model's context holds: as many replies as it writes at once, all in the
/// context it is given, which they share — <see cref="ContextTokens"/> is what all of them together hold — and the conversations it
/// keeps and the openings it holds while fewer are written. Each costs memory of its own wherever a model attends to a window of
/// what it read, and every step reads against every one of those windows, so more is not free.</param>
/// <param name="BackgroundSequences">How many replies a language model writes at once for work nobody is watching — a document
/// translated passage by passage — in a context of its own made when such work comes and let go when it stops, each with a slice
/// of <paramref name="BackgroundTokens"/> to itself; 0 for none, all its work done in the one context.</param>
/// <param name="BackgroundTokens">How long each conversation in that context may grow.</param>
/// <param name="BackgroundBytes">What that context holds on <see cref="Device"/>: it is made only where that much is free.</param>
/// <param name="HostBytes">What a model on a GPU holds in this machine's memory besides — the layers of a language model cut between
/// the GPU and the CPU, and the context they keep; the saves of its partial state (<see cref="Checkpoints"/>) — counted against the
/// host's share of it (<see cref="HostMemory"/>); 0 for a model wholly where <see cref="Device"/> places it, keeping none.</param>
/// <param name="Checkpoints">How many saves of a conversation's partial state a language model holding such state keeps — a recurrent
/// layer's state, a sliding window's cells — each where a turn ends: what lets a conversation whose start a prompt shares be cut back
/// to it and read on from there, rather than read again whole. Held in this machine's memory, and counted with
/// <see cref="HostBytes"/>; 0 for none.</param>
/// <param name="ExpertsInHost">Whether a mixture-of-experts language model on a GPU keeps its routed experts in this machine's memory,
/// the rest of it — attention, the shared weights, its context — on the GPU: each token reads a few of its experts, so the GPU holds
/// what every token reads and the CPU what few do. Their weights are counted with <see cref="HostBytes"/>.</param>
/// <param name="ExpertCacheBytes">What a model keeping its experts in this machine's memory holds of them on its GPU besides, the
/// most recently read kept there (llama.cpp's expert cache); 0 for none.</param>
/// <param name="TileCells">For a decoder, the side of the squares of latent cells it decodes a latent in, each alone and blended
/// back where they overlap, so it holds one tile's work at a time; 0 decodes the whole latent at once.</param>
public sealed record ModelSpec(string Id, string Runner, string Folder, string? File, string? Adapter, long MemoryBytes, int ContextTokens,
                               IReadOnlyDictionary<string, string>? CudaOptions = null, string? Projector = null, int? Device = null,
                               int BatchTokens = ModelSpec.DefaultBatch, ContextCache ContextCache = ContextCache.F16, int? GpuLayers = null,
                               int Sequences = ModelSpec.DefaultSequences, int BackgroundSequences = 0, int BackgroundTokens = 0, long BackgroundBytes = 0,
                               long HostBytes = 0, int Checkpoints = 0, bool ExpertsInHost = false, long ExpertCacheBytes = 0, int TileCells = 0)
{
    /// <summary>Whether <paramref name="other"/> runs the same model in the same place at the same size — a model held
    /// as one and asked for as the other is loaded again.</summary>
    public bool PlacedAs(ModelSpec other)
        => Device == other.Device && ContextTokens == other.ContextTokens && MemoryBytes == other.MemoryBytes
           && File == other.File && Folder == other.Folder && Projector == other.Projector
           && BatchTokens == other.BatchTokens && ContextCache == other.ContextCache && GpuLayers == other.GpuLayers && Sequences == other.Sequences
                      && BackgroundSequences == other.BackgroundSequences && BackgroundTokens == other.BackgroundTokens && BackgroundBytes == other.BackgroundBytes
                      && HostBytes == other.HostBytes && Checkpoints == other.Checkpoints
                      && ExpertsInHost == other.ExpertsInHost && ExpertCacheBytes == other.ExpertCacheBytes && TileCells == other.TileCells;

    /// <summary>The sequences a language model's context holds where nothing gives it room for more: as many as the catalog's figures
    /// were measured holding.</summary>
    public const int DefaultSequences = 8;

    /// <summary>The tokens a language model reads in a step where nothing gives it more room: llama.cpp's own.</summary>
    public const int DefaultBatch = 512;
}
