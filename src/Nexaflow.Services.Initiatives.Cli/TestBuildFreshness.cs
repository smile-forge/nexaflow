using System.Reflection.Metadata;
using System.Security.Cryptography;
using Nexaflow.Services.Initiatives.Product.Services;

namespace Nexaflow.Services.Initiatives.Cli;

/// <summary>
/// Answers the one question the coverage manifest cannot answer about itself: was the assembly it was built
/// from actually compiled from the source sitting in the working tree now?
/// <para>
/// The answer comes from the portable PDB, which records a content hash for every document the compiler
/// read. Comparing those against the files on disk is exact — it is the compiler's own record of what it
/// consumed — and that is precisely why this is not a timestamp check. A timestamp check fires on any file
/// git merely restored, because a checkout, merge or stash rewrites write times over byte-identical
/// content; warnings that cry wolf after every branch switch get trained away, and this one guards a
/// release gate. A PDB hash changes only when the code did.
/// </para>
/// <para>
/// Without this the gate reads clean over a lie: <c>scan-tests</c> faithfully reports whatever the last
/// build said, so a test DLL left behind its source keeps asserting <c>[CoversNode]</c> ids that the source
/// no longer declares — and a node id that has since been renamed is reported as missing from the tree
/// when it is the manifest, not the tree, that is out of date.
/// </para>
/// </summary>
internal static class TestBuildFreshness
{
    // The two document-hash algorithms a portable PDB can name (ECMA-335 / portable PDB spec).
    private static readonly Guid Sha1Guid   = new("ff1816ec-aa5e-4d10-87f7-6f4963833460");
    private static readonly Guid Sha256Guid = new("8829d00f-11b8-4213-878b-770e8597ac16");

    /// <summary>A test assembly whose sources have been edited since it was last compiled.</summary>
    internal readonly record struct StaleBuild(string Assembly, IReadOnlyList<string> ChangedFiles);

    /// <summary>
    /// Every assembly in <paramref name="dlls"/> whose PDB documents no longer match the working tree.
    /// An assembly with no PDB, or whose documents cannot be re-rooted into this checkout, is skipped:
    /// unverifiable is not the same as stale, and guessing either way would gate a release on a hunch.
    /// </summary>
    public static List<StaleBuild> Check(IEnumerable<string> dlls, string repoRoot)
    {
        var worktrees = GitWorktrees.Roots(repoRoot);
        var stale = new List<StaleBuild>();

        foreach (var dll in dlls)
        {
            var changed = ChangedDocuments(dll, repoRoot, worktrees);
            if (changed.Count > 0)
                stale.Add(new StaleBuild(Path.GetFileNameWithoutExtension(dll), changed));
        }
        return stale;
    }

    private static List<string> ChangedDocuments(string dll, string repoRoot, IReadOnlyList<string> worktrees)
    {
        var changed = new List<string>();

        var pdbPath = Path.ChangeExtension(dll, ".pdb");
        if (!File.Exists(pdbPath)) return changed;

        try
        {
            using var stream = File.OpenRead(pdbPath);
            using var provider = MetadataReaderProvider.FromPortablePdbStream(stream, MetadataStreamOptions.PrefetchMetadata);
            var reader = provider.GetMetadataReader();

            foreach (var handle in reader.Documents)
            {
                Document doc;
                try { doc = reader.GetDocument(handle); }
                catch { continue; }
                if (doc.Hash.IsNil || doc.Name.IsNil) continue;

                string name;
                try { name = reader.GetString(doc.Name); }
                catch { continue; }
                if (!name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) continue;

                // Only a file that is in THIS checkout can be compared. A deterministic "/_/" path, a
                // document from another machine, or generated output under obj/ is unverifiable.
                if (Resolve(name, repoRoot, worktrees) is not { } file) continue;
                if (!File.Exists(file) || IsGenerated(file)) continue;

                var recorded = reader.GetBlobBytes(doc.Hash);
                if (Hash(file, reader.GetGuid(doc.HashAlgorithm)) is not { } actual) continue;
                if (!actual.AsSpan().SequenceEqual(recorded)) changed.Add(Relative(file, repoRoot));
            }
        }
        catch { /* an unreadable PDB is unverifiable, not stale */ }

        return changed;
    }

    private static byte[]? Hash(string file, Guid algorithm)
    {
        try
        {
            var bytes = File.ReadAllBytes(file);
            if (algorithm == Sha256Guid) return SHA256.HashData(bytes);
            if (algorithm == Sha1Guid) return SHA1.HashData(bytes);
            return null;   // an algorithm we do not know is unverifiable, not a mismatch
        }
        catch { return null; }
    }

    /// <summary>
    /// A PDB document path made absolute inside this checkout — re-rooting one recorded by a build that ran
    /// in a linked worktree, exactly as the collector re-roots the paths it records.
    /// </summary>
    private static string? Resolve(string docName, string repoRoot, IReadOnlyList<string> worktrees)
    {
        if (GitWorktrees.TryReRoot(docName, repoRoot, worktrees, out var reRooted))
            return Path.Combine(repoRoot, reRooted.Replace('/', Path.DirectorySeparatorChar));

        var p = docName.Replace('\\', '/');
        var root = repoRoot.Replace('\\', '/').TrimEnd('/');
        if (p.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase)) return docName;

        return null;
    }

    private static bool IsGenerated(string file) =>
        file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);

    private static string Relative(string file, string repoRoot) =>
        Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
}
