using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Nexaflow.Syntax.Compiler;

/// <summary>
/// Loads a project's analyzers and source generators. Each analyzer directory gets a load context of its own, so two
/// projects referencing different versions of the same package each get theirs; the compiler's own assemblies are always
/// the host's, because an analyzer talks to the compiler it runs in.
/// <para>
/// A directory's context is replaced once anything it loaded is rebuilt. A rebuild keeps an assembly's name and changes its
/// identity, and a context holding the old build refuses the new one ("Assembly with same name is already loaded") — which
/// Roslyn reports as an analyzer that would not load rather than as an exception, so every rule in it would find nothing for
/// the rest of the process's life. Projects still holding the old build keep it until they next reload.
/// </para>
/// </summary>
internal sealed class AnalyzerLoader : IAnalyzerAssemblyLoader
{
    private readonly Dictionary<string, DirectoryContext> _contexts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The assemblies the host loads for itself, by simple name.</summary>
    private static readonly HashSet<string> HostAssemblies =
        new(((AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string) ?? "")
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Select(Path.GetFileNameWithoutExtension)
                .OfType<string>(),
            StringComparer.OrdinalIgnoreCase);

    public void AddDependencyLocation(string fullPath) { }

    public Assembly LoadFromPath(string fullPath)
    {
        var image     = AnalyzerImage.Read(fullPath);
        var directory = Path.GetDirectoryName(fullPath) ?? "";

        DirectoryContext? context;
        lock (_contexts)
        {
            if (!_contexts.TryGetValue(directory, out context) || !context.Takes(image))
                _contexts[directory] = context = new DirectoryContext(directory);
        }
        return context.Take(image);
    }

    /// <summary>An assembly file as read: its bytes, and the name and build that decide which context can take it.</summary>
    private sealed record AnalyzerImage(string Location, byte[] Bytes, string Name, Guid Mvid, DateTime WrittenUtc)
    {
        public static AnalyzerImage Read(string location)
        {
            var written  = File.GetLastWriteTimeUtc(location);
            var bytes    = File.ReadAllBytes(location);
            using var pe = new PEReader(ImmutableCollectionsMarshal.AsImmutableArray(bytes));
            var metadata = pe.GetMetadataReader();
            return new AnalyzerImage(location, bytes, metadata.GetString(metadata.GetAssemblyDefinition().Name),
                                     metadata.GetGuid(metadata.GetModuleDefinition().Mvid), written);
        }
    }

    /// <summary>
    /// One analyzer directory's assemblies, loaded from their bytes rather than their paths: a generator this repository
    /// builds itself would otherwise stay locked for the life of the resident process, and its next build would fail.
    /// </summary>
    private sealed class DirectoryContext(string directory) : AssemblyLoadContext($"analyzers: {directory}")
    {
        private readonly ConcurrentDictionary<string, AnalyzerImage> _loaded = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Whether this context can take <paramref name="image"/>: it holds no other build of that name, and nothing it has
        /// loaded has been rebuilt since — an analyzer that was not rebuilt would otherwise go on binding to the old build of a
        /// dependency that was.
        /// </summary>
        public bool Takes(AnalyzerImage image) =>
            (!_loaded.TryGetValue(image.Name, out var held) || held.Mvid == image.Mvid)
            && _loaded.Values.All(loaded => File.GetLastWriteTimeUtc(loaded.Location) == loaded.WrittenUtc);

        public Assembly Take(AnalyzerImage image)
        {
            using var stream = new MemoryStream(image.Bytes, writable: false);
            var assembly = LoadFromStream(stream);
            _loaded.TryAdd(image.Name, image);
            return assembly;
        }

        protected override Assembly? Load(AssemblyName name)
        {
            // The host's own copy wherever it has one — the compiler an analyzer talks to, and the runtime — and the one beside
            // the analyzer otherwise. Not by name: the SDK's analyzers depend on Microsoft.CodeAnalysis.NetAnalyzers, which the
            // host does not have, and sending that to the host left every CA rule unloaded.
            if (name.Name is not { } simple || HostAssemblies.Contains(simple)) return null;

            var beside = Path.Combine(directory, simple + ".dll");
            return File.Exists(beside) ? Take(AnalyzerImage.Read(beside)) : null;
        }
    }
}

/// <summary>
/// The <c>.editorconfig</c> / <c>.globalconfig</c> options a project hands its generators — where MSBuild puts the
/// <c>build_property.*</c> values several generators read to decide what to emit.
/// </summary>
internal sealed class ConfigOptionsProvider(AnalyzerConfigSet set) : AnalyzerConfigOptionsProvider
{
    public override AnalyzerConfigOptions GlobalOptions { get; } = new Options(set.GlobalConfigOptions.AnalyzerOptions);

    public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) =>
        new Options(set.GetOptionsForSourcePath(tree.FilePath).AnalyzerOptions);

    public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) =>
        new Options(set.GetOptionsForSourcePath(textFile.Path).AnalyzerOptions);

    private sealed class Options(ImmutableDictionary<string, string> values) : AnalyzerConfigOptions
    {
        public override IEnumerable<string> Keys => values.Keys;

        public override bool TryGetValue(string key, [NotNullWhen(true)] out string? value) =>
            values.TryGetValue(key, out value);
    }
}

/// <summary>A file a generator is handed as input rather than compiled — a view, a resource list.</summary>
internal sealed class FileText(string path) : AdditionalText
{
    public override string Path { get; } = path;

    public override SourceText? GetText(CancellationToken cancellationToken = default) =>
        File.Exists(Path) ? SourceText.From(File.ReadAllText(Path)) : null;
}

/// <summary>A file as an edit would leave it, handed to analyzers before it is written.</summary>
internal sealed class MemoryText(string path, string text) : AdditionalText
{
    private readonly SourceText _text = SourceText.From(text);

    public override string Path { get; } = path;

    public override SourceText GetText(CancellationToken cancellationToken = default) => _text;
}
