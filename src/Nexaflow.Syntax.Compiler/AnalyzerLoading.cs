using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Nexaflow.Syntax.Compiler;

/// <summary>
/// Loads a project's source generators. Each analyzer directory gets a load context of its own, so two projects
/// referencing different versions of the same generator package each get theirs; the compiler's own assemblies are
/// always the host's, because a generator talks to the compiler it runs in.
/// </summary>
internal sealed class AnalyzerLoader : IAnalyzerAssemblyLoader
{
    private readonly ConcurrentDictionary<string, DirectoryContext> _contexts = new(StringComparer.OrdinalIgnoreCase);

    public void AddDependencyLocation(string fullPath) { }

    public Assembly LoadFromPath(string fullPath) =>
        _contexts.GetOrAdd(Path.GetDirectoryName(fullPath) ?? "", directory => new DirectoryContext(directory))
                 .LoadFromBytes(fullPath);

    /// <summary>
    /// One analyzer directory's assemblies, loaded from their bytes rather than their paths: a generator this repository
    /// builds itself would otherwise stay locked for the life of the resident process, and its next build would fail.
    /// </summary>
    private sealed class DirectoryContext(string directory) : AssemblyLoadContext($"analyzers: {directory}")
    {
        public Assembly LoadFromBytes(string path)
        {
            using var image = new MemoryStream(File.ReadAllBytes(path));
            return LoadFromStream(image);
        }

        protected override Assembly? Load(AssemblyName name)
        {
            if (name.Name is not { } simple
                || simple.StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal)
                || simple.StartsWith("System.", StringComparison.Ordinal))
                return null;

            var beside = Path.Combine(directory, simple + ".dll");
            return File.Exists(beside) ? LoadFromBytes(beside) : null;
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
