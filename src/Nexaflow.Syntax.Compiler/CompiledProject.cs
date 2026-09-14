using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
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

    /// <summary>What was reported the last few times it was asked, by everything the answer was a function of.</summary>
    private readonly Dictionary<string, IReadOnlyList<CompileDiagnostic>> _reported = new(StringComparer.Ordinal);

    private CSharpCommandLineArguments? _args;
    private CSharpCompilation? _compilation;
    private GeneratorDriver? _driver;
    private ImmutableArray<DiagnosticAnalyzer>? _analyzers;
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

    /// <summary>Every reference its command line names, whether or not it is on disk.</summary>
    public IReadOnlyCollection<string> Declared => _declared;

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
        // Without a restore the design-time build still hands out a command line — one with no package in it. Compiled, every
        // use of a package type is an error, and an edit adding one more use "introduces" it: a false alarm that reads exactly
        // like a real one. Nothing about such a project can be said, so it says that instead.
        if (!Restored())
            return $"its packages are not restored, so nothing it uses from them can be read - `dotnet restore {Path.GetFileName(ProjectPath)}` "
                 + "(or a build) and it is checked from then on";

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
            _reported.Clear();
            _analyzers   = null;
            _driver      = CreateDriver(parsed, loader);
            AssemblyFileName = parsed.OutputFileName ?? parsed.CompilationName + ".dll";
        }

        Sync(cancellation);
        return null;
    }

    /// <summary>Whether restore has written this project's assets file, which every SDK project has once restored.</summary>
    private bool Restored()
    {
        var obj = Path.Combine(Directory, "obj");
        try
        {
            return System.IO.Directory.Exists(obj)
                && System.IO.Directory.EnumerateFiles(obj, "project.assets.json", SearchOption.AllDirectories).Any();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return true; }   // cannot tell: try it
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

    /// <summary>
    /// Everything this project's compilation is a function of on disk, as one value: its command line, and the write
    /// time of every source, reference and additional file. Two askings with the same value get the same answer.
    /// </summary>
    public string Fingerprint()
    {
        long acc = _trees.Count * 31L + _references.Count;
        foreach (var (path, (_, written)) in _trees) acc += HashCode.Combine(path.ToUpperInvariant(), written.Ticks);
        foreach (var (path, (_, written)) in _references) acc += HashCode.Combine(path.ToUpperInvariant(), written.Ticks);
        foreach (var file in _args?.AdditionalFiles ?? [])
        {
            var full = Path.GetFullPath(file.Path, Directory);
            acc += HashCode.Combine(full.ToUpperInvariant(), File.Exists(full) ? File.GetLastWriteTimeUtc(full).Ticks : 0);
        }
        return $"{_stamp}:{acc:x}";
    }

    /// <summary>
    /// What the compiler and this project's analyzers report about <paramref name="compilation"/> — this project's, with
    /// whatever references it was given. Warnings and errors, or everything <paramref name="ids"/> names at any severity;
    /// never what the project suppresses. <paramref name="key"/> is what the compilation is a function of, under which the
    /// answer is kept.
    /// <para>
    /// The compiler's own diagnostics are left out when <paramref name="ids"/> could not name one (it does not mention
    /// CS), because working them out means binding every method body in the project and an analyzer id does not need it.
    /// </para>
    /// </summary>
    public IReadOnlyList<CompileDiagnostic> Diagnostics(CSharpCompilation compilation, string key, AnalyzerLoader loader,
                                                        Regex? ids, CancellationToken cancellation)
    {
        var asked = $"{key}|{ids}";
        if (_reported.TryGetValue(asked, out var kept)) return kept;

        var diagnostics = new List<Diagnostic>();
        if (ids is null || ids.ToString().Contains("CS", StringComparison.OrdinalIgnoreCase))
            diagnostics.AddRange(compilation.GetDiagnostics(cancellation));

        var analyzers = AnalyzersFor(loader, ids);
        if (analyzers.Length > 0)
        {
            var args    = _args!;
            var configs = args.AnalyzerConfigPaths.Where(File.Exists)
                              .Select(p => AnalyzerConfig.Parse(File.ReadAllText(p), p)).ToList();
            var options = new AnalyzerOptions(
                [.. args.AdditionalFiles.Select(f => (AdditionalText)new FileText(Path.GetFullPath(f.Path, Directory)))],
                new ConfigOptionsProvider(AnalyzerConfigSet.Create(configs)));

            var withAnalyzers = compilation.WithAnalyzers(analyzers, new CompilationWithAnalyzersOptions(
                options, onAnalyzerException: null, concurrentAnalysis: true, logAnalyzerExecutionTime: false));
            diagnostics.AddRange(withAnalyzers.GetAnalyzerDiagnosticsAsync(cancellation).GetAwaiter().GetResult());
        }

        var found = new List<CompileDiagnostic>();
        foreach (var diagnostic in diagnostics.Distinct())
        {
            if (diagnostic.IsSuppressed) continue;
            if (ids is not null ? !ids.IsMatch(diagnostic.Id) : diagnostic.Severity < DiagnosticSeverity.Warning) continue;

            var span = diagnostic.Location.GetLineSpan();
            var (path, line, column) = span.IsValid && span.Path is { Length: > 0 } p
                ? (Path.GetFullPath(p, Directory), span.StartLinePosition.Line + 1, span.StartLinePosition.Character + 1)
                : (ProjectPath, 1, 1);   // about the project rather than a place in it: a generator that would not load
            found.Add(new CompileDiagnostic(path, line, column, diagnostic.Id, diagnostic.Severity.ToString().ToLowerInvariant(),
                                            diagnostic.GetMessage(CultureInfo.InvariantCulture)));
        }

        if (_reported.Count >= 16) _reported.Clear();
        return _reported[asked] = [.. found.Distinct().OrderBy(d => d.FullPath, StringComparer.OrdinalIgnoreCase).ThenBy(d => d.Line).ThenBy(d => d.Column)];
    }

    /// <summary>The project's analyzers that report an id <paramref name="ids"/> matches — all of them when it is null.</summary>
    private ImmutableArray<DiagnosticAnalyzer> AnalyzersFor(AnalyzerLoader loader, Regex? ids)
    {
        _analyzers ??= [.. _args!.AnalyzerReferences.SelectMany(reference =>
        {
            try
            {
                return new AnalyzerFileReference(Path.GetFullPath(reference.FilePath, Directory), loader).GetAnalyzers(LanguageNames.CSharp);
            }
            catch (Exception ex) when (ex is IOException or BadImageFormatException or FileLoadException
                                                          or TypeLoadException or ReflectionTypeLoadException)
            {
                return [];
            }
        })];

        if (ids is null) return _analyzers.Value;
        return [.. _analyzers.Value.Where(analyzer =>
        {
            try { return analyzer.SupportedDiagnostics.Any(d => ids.IsMatch(d.Id)); }
            catch { return false; }   // an analyzer that cannot say what it reports is not one asked for
        })];
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