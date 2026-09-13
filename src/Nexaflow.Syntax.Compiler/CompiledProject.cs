using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Nexaflow.Syntax.Compiler;

/// <summary>
/// One project held compiled in memory: its compilation as the files on disk stand, and the generators that run
/// over it. Kept current by write time rather than rebuilt — a file whose write time has not moved keeps its parse,
/// and a reference whose has is swapped for the new one — so a warm check costs the files that changed.
/// </summary>
internal sealed class CompiledProject
{
    /// <summary>A reference assembly is the same bytes in every project that names it, so it is read once.</summary>
    private static readonly ConcurrentDictionary<(string Path, long Ticks), AssemblyMetadata> Metadata = new();

    private readonly Dictionary<string, (SyntaxTree Tree, DateTime WrittenUtc)> _trees = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (MetadataReference Reference, DateTime WrittenUtc)> _references =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every reference the command line names, built or not: a project referenced before it has been built still
    /// counts as one this project compiles against, and a compilation of it can stand in for the missing file.</summary>
    private readonly HashSet<string> _declared = new(StringComparer.OrdinalIgnoreCase);

    private CSharpCommandLineArguments? _args;
    private CSharpCompilation? _compilation;
    private GeneratorDriver? _driver;
    private string _stamp = "";

    private CompiledProject(string projectPath)
    {
        ProjectPath = projectPath;
        Directory   = Path.GetDirectoryName(projectPath)!;
    }

    public string ProjectPath { get; }
    public string Directory { get; }
    public string Name => Path.GetFileNameWithoutExtension(ProjectPath);

    /// <summary>The file name this project's assembly is built as — what a consumer's reference list names it by.</summary>
    public string AssemblyFileName { get; private set; } = "";

    public IEnumerable<string> SourcePaths => _trees.Keys;

    public static (CompiledProject? Project, string? Error) Load(string csproj, AnalyzerLoader loader,
                                                                 CancellationToken cancellation)
    {
        var project = new CompiledProject(csproj);
        return project.Refresh(loader, cancellation) is { } error ? (null, error) : (project, null);
    }

    /// <summary>
    /// The references this project declares that are <paramref name="other"/>'s assembly. Found by the assembly's file
    /// name under that project's directory rather than by one exact path, because a consumer names whichever output the
    /// build chose for it — the assembly in bin, or the reference assembly beside the intermediate one in obj.
    /// </summary>
    public IReadOnlyList<string> ReferencesTo(CompiledProject other) =>
        [.. _declared.Where(path => Path.GetFileName(path).Equals(other.AssemblyFileName, StringComparison.OrdinalIgnoreCase)
                                 && path.StartsWith(other.Directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))];

    /// <summary>
    /// Brings the project up to what is on disk: the whole command line when anything that decides it has moved,
    /// otherwise just the files and references whose write times have.
    /// </summary>
    public string? Refresh(AnalyzerLoader loader, CancellationToken cancellation)
    {
        var (arguments, error) = DesignTimeBuild.For(ProjectPath, cancellation);
        if (arguments is null) return error;

        if (arguments.Stamp != _stamp || _args is null)
        {
            var parsed = CSharpCommandLineParser.Default.Parse(arguments.Args, Directory, sdkDirectory: null);
            if (parsed.SourceFiles.IsEmpty) return "its compiler command line names no sources";

            _args        = parsed;
            _stamp       = arguments.Stamp;
            _compilation = null;
            _trees.Clear();
            _references.Clear();
            _declared.Clear();
            _driver      = CreateDriver(parsed, loader);
            AssemblyFileName = parsed.OutputFileName ?? parsed.CompilationName + ".dll";
        }

        Sync(cancellation);
        return null;
    }

    /// <summary>
    /// This project's compilation with some files' text in place of what is on disk — null for a file that is not
    /// there — and, optionally, other projects' assemblies swapped for compilations of them. Generators run over the
    /// result, so what they emit follows the change too.
    /// </summary>
    public CSharpCompilation With(IReadOnlyDictionary<string, string?> texts, CancellationToken cancellation,
                                  IReadOnlyDictionary<string, MetadataReference>? swaps = null)
    {
        var compilation = _compilation!;

        foreach (var (path, text) in texts)
        {
            var held = _trees.TryGetValue(path, out var h) ? h.Tree : null;
            if (text is null)
            {
                if (held is not null) compilation = compilation.RemoveSyntaxTrees(held);
                continue;
            }

            var tree = Parse(path, text, cancellation);
            compilation = held is null ? compilation.AddSyntaxTrees(tree) : compilation.ReplaceSyntaxTree(held, tree);
        }

        foreach (var (output, replacement) in swaps ?? new Dictionary<string, MetadataReference>())
            compilation = _references.TryGetValue(output, out var existing)
                ? compilation.ReplaceReference(existing.Reference, replacement)
                : _declared.Contains(output) ? compilation.AddReferences(replacement) : compilation;

        if (_driver is null) return compilation;

        _driver = _driver.RunGeneratorsAndUpdateCompilation(compilation, out var generated, out _, cancellation);
        return (CSharpCompilation)generated;
    }

    /// <summary>The errors the compiler reports in <paramref name="paths"/>, located by file, line and column.</summary>
    public static IReadOnlyList<CompileProblem> ErrorsIn(CSharpCompilation compilation, IReadOnlySet<string> paths,
                                                         CancellationToken cancellation)
    {
        var problems = new List<CompileProblem>();
        foreach (var tree in compilation.SyntaxTrees.Where(t => paths.Contains(t.FilePath)))
            foreach (var diagnostic in compilation.GetSemanticModel(tree).GetDiagnostics(cancellationToken: cancellation))
            {
                if (diagnostic.Severity != DiagnosticSeverity.Error || !diagnostic.Location.IsInSource) continue;

                var at = diagnostic.Location.GetLineSpan().StartLinePosition;
                problems.Add(new CompileProblem(tree.FilePath, at.Line + 1, at.Character + 1, diagnostic.Id,
                                                diagnostic.GetMessage(CultureInfo.InvariantCulture)));
            }
        return problems;
    }

    private void Sync(CancellationToken cancellation)
    {
        var args        = _args!;
        var compilation = _compilation ?? CSharpCompilation.Create(args.CompilationName, options: args.CompilationOptions);

        var sources = args.SourceFiles.Select(f => Path.GetFullPath(f.Path, Directory))
                          .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var gone in _trees.Keys.Where(k => !sources.Contains(k)).ToList())
        {
            compilation = compilation.RemoveSyntaxTrees(_trees[gone].Tree);
            _trees.Remove(gone);
        }

        foreach (var path in sources)
        {
            var written = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
            if (_trees.TryGetValue(path, out var held) && held.WrittenUtc == written) continue;

            var tree = Parse(path, written == DateTime.MinValue ? "" : File.ReadAllText(path), cancellation);
            compilation = held.Tree is null ? compilation.AddSyntaxTrees(tree) : compilation.ReplaceSyntaxTree(held.Tree, tree);
            _trees[path] = (tree, written);
        }

        foreach (var reference in args.MetadataReferences)
        {
            var path = Path.GetFullPath(reference.Reference, Directory);
            _declared.Add(path);
            if (!File.Exists(path)) continue;

            var written = File.GetLastWriteTimeUtc(path);
            if (_references.TryGetValue(path, out var held) && held.WrittenUtc == written) continue;

            // Read into memory, never mapped: a mapped file stays open for as long as the compilation lives, and the next
            // `dotnet build` of that project then fails to overwrite its own output.
            var metadata = Metadata.GetOrAdd((path, written.Ticks), key => AssemblyMetadata.CreateFromImage(File.ReadAllBytes(key.Path)));
            var fresh    = metadata.GetReference(aliases: reference.Properties.Aliases,
                                                 embedInteropTypes: reference.Properties.EmbedInteropTypes, filePath: path);
            compilation = held.Reference is null ? compilation.AddReferences(fresh) : compilation.ReplaceReference(held.Reference, fresh);
            _references[path] = (fresh, written);
        }

        _compilation = compilation;
    }

    /// <summary>
    /// Parsed with doc comments read as structure, whatever the project asks: a <c>cref</c> is a use of what it names,
    /// and only a structured comment lets the compiler say so.
    /// </summary>
    private SyntaxTree Parse(string path, string text, CancellationToken cancellation) =>
        CSharpSyntaxTree.ParseText(SourceText.From(text, Encoding.UTF8), ParseOptionsOf(_args!), path, cancellation);

    private static CSharpParseOptions ParseOptionsOf(CSharpCommandLineArguments args) =>
        args.ParseOptions.DocumentationMode == DocumentationMode.None
            ? args.ParseOptions.WithDocumentationMode(DocumentationMode.Parse)
            : args.ParseOptions;

    private GeneratorDriver? CreateDriver(CSharpCommandLineArguments args, AnalyzerLoader loader)
    {
        var generators = new List<ISourceGenerator>();
        foreach (var analyzer in args.AnalyzerReferences)
        {
            try
            {
                generators.AddRange(new AnalyzerFileReference(Path.GetFullPath(analyzer.FilePath, Directory), loader)
                                        .GetGenerators(LanguageNames.CSharp));
            }
            catch (Exception ex) when (ex is IOException or BadImageFormatException or FileLoadException
                                                          or TypeLoadException or ReflectionTypeLoadException)
            {
                // A generator that will not load is missing from both sides of every comparison, so what it would have
                // emitted cannot show up as an error the edit introduced.
            }
        }
        if (generators.Count == 0) return null;

        var configs = args.AnalyzerConfigPaths.Where(File.Exists)
                          .Select(p => AnalyzerConfig.Parse(File.ReadAllText(p), p)).ToList();
        var additional = args.AdditionalFiles.Select(f => (AdditionalText)new FileText(Path.GetFullPath(f.Path, Directory)))
                             .ToList();

        return CSharpGeneratorDriver.Create(generators, additional, ParseOptionsOf(args),
                                            new ConfigOptionsProvider(AnalyzerConfigSet.Create(configs)));
    }
}
