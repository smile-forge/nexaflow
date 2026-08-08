namespace Nexaflow.IO.Pe;

/// <summary>
/// What <see cref="PeReader"/> should parse. Everything is on by default; a caller that only wants
/// headers (a fast "is this managed?" probe, say) can switch the expensive walks off rather than
/// pay for a full resource tree.
/// </summary>
public sealed record PeReadOptions
{
    public static readonly PeReadOptions Default = new();

    /// <summary>Headers and the section table only — the cheapest useful read.</summary>
    public static readonly PeReadOptions HeadersOnly = new()
    {
        IncludeImports       = false,
        IncludeExports       = false,
        IncludeResources     = false,
        IncludeEntropy       = false,
        IncludeSectionHashes = false,
        IncludeFileHashes    = false,
        VerifySignature      = false,
    };

    public bool IncludeImports       { get; init; } = true;
    public bool IncludeExports       { get; init; } = true;
    public bool IncludeResources     { get; init; } = true;
    public bool IncludeEntropy       { get; init; } = true;
    public bool IncludeSectionHashes { get; init; } = true;

    /// <summary>SHA-256 and MD5 of the whole file. Like the entropy sweep this is a full-file pass,
    /// so switch it off when only the structure is wanted.</summary>
    public bool IncludeFileHashes { get; init; } = true;

    /// <summary>
    /// Ask the OS for the Authenticode verdict (<c>WinVerifyTrust</c>). Off by default because it can
    /// touch the network for revocation checking — the inspector turns it on from a background task,
    /// never during the initial parse.
    /// </summary>
    public bool VerifySignature { get; init; }

    /// <summary>How many slices the entropy sweep produces. Drives heatmap resolution.</summary>
    public int EntropyBuckets { get; init; } = 512;

    /// <summary>Refuse to materialise a single resource larger than this (guards a corrupt size field).</summary>
    public int MaxResourceBytes { get; init; } = 64 * 1024 * 1024;

    /// <summary>Cap on entries parsed from any one table, so a corrupt count cannot spin forever.</summary>
    public int MaxTableEntries { get; init; } = 200_000;
}
