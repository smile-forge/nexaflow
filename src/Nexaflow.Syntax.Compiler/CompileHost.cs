using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Nexaflow.Syntax.Compiler;

/// <summary>A source file as an edit found it and as it leaves it — null for a file that is not there.</summary>
public sealed record SourceChange(string FullPath, string? Before, string? After);

/// <summary>One compiler error, where it is.</summary>
public sealed record CompileProblem(string FullPath, int Line, int Column, string Id, string Message);

/// <summary>One thing the compiler or an analyzer reported, where it is — a project-wide one at its project file.</summary>
public sealed record CompileDiagnostic(string FullPath, int Line, int Column, string Id, string Severity, string Message);

/// <summary>What was reported, and what could not be asked and why.</summary>
public sealed record DiagnosticReport(IReadOnlyList<CompileDiagnostic> Found, IReadOnlyList<string> NotChecked, TimeSpan Elapsed);

/// <summary>What an edit did to the build.</summary>
/// <param name="Checked">The projects compiled for the answer.</param>
/// <param name="Introduced">Errors after the edit that were not there before it.</param>
/// <param name="Fixed">Errors before the edit that are gone after it.</param>
/// <param name="NotChecked">What could not be checked, and why — so a clean answer is never mistaken for a
/// complete one.</param>
public sealed record CompileReport(IReadOnlyList<string> Checked, IReadOnlyList<CompileProblem> Introduced,
                                   IReadOnlyList<CompileProblem> Fixed, IReadOnlyList<string> NotChecked, TimeSpan Elapsed);

/// <summary>Where a symbol is declared and used, by file and character span.</summary>
public sealed record SymbolLocation(string FullPath, int Start, int Length);

/// <param name="Symbol">The symbol found at the position asked about, as the compiler names it — null when there
/// was none, with <paramref name="Error"/> saying why.</param>
/// <param name="Unsearched">Candidate files whose project could not be searched, so a caller that must not miss a use
/// can fall back to something coarser for those.</param>
public sealed record ReferenceReport(string? Symbol, IReadOnlyList<SymbolLocation> Locations, IReadOnlyList<string> NotChecked,
                                     string? Error, IReadOnlyList<string>? Unsearched = null);

/// <summary>
/// Asks the compiler what an edit did. Projects are loaded on first use and kept, so the host is meant to live as
/// long as the process that owns it — the resident <c>nfi</c> process, where a warm check costs a fraction of a second.
/// <para>
/// Every answer is a comparison: the errors in the files concerned with the edit's before-text against the same files
/// with its after-text. An error that was there already — a generator that did not load, a reference that has not
/// been built — is on both sides and cancels out, so what is reported is what the edit did, not the state of the
/// machine.
/// </para>
/// </summary>
public sealed class CompileHost
{
    private readonly Dictionary<string, CompiledProject> _projects = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string?> _owners = new(StringComparer.OrdinalIgnoreCase);
    private readonly AnalyzerLoader _loader = new();
    private readonly object _gate = new();
    private readonly string? _boundary;

    /// <param name="boundary">The directory a search for a file's project stops at — the repository, so a temp file is
    /// never claimed by a project somewhere above it.</param>
    public CompileHost(string? boundary = null) =>
        _boundary = boundary is { Length: > 0 } ? Path.TrimEndingDirectorySeparator(Path.GetFullPath(boundary)) : null;

    /// <summary>
    /// The errors <paramref name="changes"/> introduced and fixed: in the projects the changed files belong to, and in
    /// <paramref name="consumerFiles"/> — the files elsewhere that name what changed — when their project compiles
    /// against one of those.
    /// </summary>
    /// <param name="budget">How long loading projects may take. A project already loaded is always checked; one that
    /// is not is loaded only while the check is inside its budget, and named in <see cref="CompileReport.NotChecked"/>
    /// when it is not.</param>
    public CompileReport Check(IReadOnlyList<SourceChange> changes, IReadOnlyCollection<string> consumerFiles,
                               TimeSpan budget, CancellationToken cancellation = default)
    {
        lock (_gate)
        {
            var clock      = Stopwatch.StartNew();
            var checkedOn  = new List<string>();
            var introduced = new List<CompileProblem>();
            var fixedOnes  = new List<CompileProblem>();
            var notChecked = new List<string>();

            if (changes.Any(c => c.FullPath.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase)))
                notChecked.Add("XAML: its generated code is produced by the build, so a view is checked only through the "
                             + "C# that names it");

            var byProject = new Dictionary<string, List<SourceChange>>(StringComparer.OrdinalIgnoreCase);
            foreach (var change in changes.Where(c => c.FullPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)))
            {
                if (ProjectOf(change.FullPath) is not { } csproj)
                {
                    notChecked.Add($"{Path.GetFileName(change.FullPath)}: no project compiles it");
                    continue;
                }
                (byProject.TryGetValue(csproj, out var list) ? list : byProject[csproj] = []).Add(change);
            }

            var loaded = new List<(CompiledProject Project, List<SourceChange> Changes)>();
            foreach (var (csproj, list) in byProject)
                if (Checkable(csproj, notChecked) && Get(csproj, clock, budget, notChecked, cancellation) is { } project) loaded.Add((project, list));

            var built  = new Dictionary<string, (CSharpCompilation Compilation, string Key)?>(StringComparer.OrdinalIgnoreCase);
            var edited = new List<(CompiledProject Project, CSharpCompilation Before, CSharpCompilation After)>();
            foreach (var (project, list) in DependenciesFirst(loaded))
            {
                var behind = Behind(project, clock, budget, notChecked, cancellation, built, out _);
                var (swapBefore, swapAfter) = Swaps(project, edited);
                var before  = project.With(list.ToDictionary(c => c.FullPath, c => c.Before, StringComparer.OrdinalIgnoreCase), cancellation, Over(behind, swapBefore));
                var after   = project.With(list.ToDictionary(c => c.FullPath, c => c.After, StringComparer.OrdinalIgnoreCase), cancellation, Over(behind, swapAfter));
                var targets = list.Select(c => c.FullPath)
                                  .Concat(consumerFiles.Where(f => string.Equals(ProjectOf(f), project.ProjectPath, StringComparison.OrdinalIgnoreCase)))
                                  .ToHashSet(StringComparer.OrdinalIgnoreCase);

                Compare(CompiledProject.ErrorsIn(before, targets, cancellation),
                        CompiledProject.ErrorsIn(after, targets, cancellation), introduced, fixedOnes);
                edited.Add((project, before, after));
                checkedOn.Add(project.Name);
            }

            foreach (var group in consumerFiles.GroupBy(f => ProjectOf(f) ?? "", StringComparer.OrdinalIgnoreCase))
            {
                if (group.Key.Length == 0 || byProject.ContainsKey(group.Key) || edited.Count == 0) continue;
                if (!Checkable(group.Key, notChecked) || Get(group.Key, clock, budget, notChecked, cancellation) is not { } consumer) continue;

                // Only a project that compiles against an edited one can be broken by it; one that merely mentions a
                // name of the same spelling is about something else.
                var (before, after) = Swaps(consumer, edited);
                if (before.Count == 0) continue;

                var behind  = Behind(consumer, clock, budget, notChecked, cancellation, built, out _);
                var targets = group.ToHashSet(StringComparer.OrdinalIgnoreCase);
                Compare(CompiledProject.ErrorsIn(consumer.With(NoTexts, cancellation, Over(behind, before)), targets, cancellation),
                        CompiledProject.ErrorsIn(consumer.With(NoTexts, cancellation, Over(behind, after)), targets, cancellation),
                        introduced, fixedOnes);
                checkedOn.Add(consumer.Name);
            }

            return new CompileReport(checkedOn, introduced, fixedOnes, Summarised(notChecked), clock.Elapsed);
        }
    }

    /// <summary>
    /// Every declaration of, and reference to, the symbol declared at <paramref name="position"/> in
    /// <paramref name="declarationFile"/>: in its own project, and in <paramref name="candidateFiles"/> elsewhere whose
    /// project compiles against it. Found by the compiler's binding rather than by spelling, so a different member that
    /// happens to share the name is left alone — and overrides and interface implementations of it are included.
    /// </summary>
    public ReferenceReport FindReferences(string declarationFile, int position, IReadOnlyCollection<string> candidateFiles,
                                          TimeSpan budget, CancellationToken cancellation = default)
    {
        lock (_gate)
        {
            var clock      = Stopwatch.StartNew();
            var notChecked = new List<string>();

            if (ProjectOf(declarationFile) is not { } csproj)
                return new ReferenceReport(null, [], notChecked, $"no project compiles {Path.GetFileName(declarationFile)}");
            if (Get(csproj, clock, budget, notChecked, cancellation) is not { } project)
                return new ReferenceReport(null, [], notChecked, string.Join("; ", notChecked));

            var none        = new Dictionary<string, string?>();
            var compilation = project.With(none, cancellation);
            var tree        = compilation.SyntaxTrees.FirstOrDefault(t => string.Equals(t.FilePath, declarationFile, StringComparison.OrdinalIgnoreCase));
            if (tree is null) return new ReferenceReport(null, [], notChecked, $"{project.Name} does not compile {Path.GetFileName(declarationFile)}");

            if (DeclaredAt(compilation.GetSemanticModel(tree), tree, position, cancellation) is not { } symbol)
                return new ReferenceReport(null, [], notChecked, "nothing is declared at that position");

            var locations = new List<SymbolLocation>();
            Scan(compilation, project.SourcePaths, symbol, locations, cancellation);

            var unsearched = new List<string>();
            foreach (var group in candidateFiles.GroupBy(f => ProjectOf(f) ?? "", StringComparer.OrdinalIgnoreCase))
            {
                if (group.Key.Length == 0 || string.Equals(group.Key, csproj, StringComparison.OrdinalIgnoreCase)) continue;
                if (Get(group.Key, clock, budget, notChecked, cancellation) is not { } consumer)
                {
                    unsearched.AddRange(group);
                    continue;
                }

                var swaps = consumer.ReferencesTo(project)
                                    .ToDictionary(o => o, _ => (MetadataReference)compilation.ToMetadataReference(),
                                                  StringComparer.OrdinalIgnoreCase);
                if (swaps.Count == 0) continue;

                Scan(consumer.With(none, cancellation, swaps), group, symbol, locations, cancellation);
            }

            return new ReferenceReport(symbol.ToDisplayString(), [.. locations.Distinct()], Summarised(notChecked), null, unsearched);
        }
    }

    /// <summary>
    /// What the compiler and each project's analyzers report: for the whole of each of <paramref name="projects"/>, and for
    /// <paramref name="files"/> in whichever projects compile them — a view included, which reaches its project's analyzers
    /// as an additional file. Warnings and errors, or everything <paramref name="ids"/> names at any severity.
    /// <para>
    /// A project is compiled as its sources stand, with any project it references that is not built, or is older than its
    /// sources, read from those sources too — the same answer <c>dotnet build</c> would give now, without building.
    /// </para>
    /// </summary>
    public DiagnosticReport Diagnostics(IReadOnlyCollection<string> files, IReadOnlyCollection<string> projects, Regex? ids,
                                        TimeSpan budget, CancellationToken cancellation = default)
    {
        lock (_gate)
        {
            var clock      = Stopwatch.StartNew();
            var notChecked = new List<string>();
            var found      = new List<CompileDiagnostic>();

            // Each project, with the files of it that were asked about - null for all of it.
            var wanted = new Dictionary<string, HashSet<string>?>(StringComparer.OrdinalIgnoreCase);
            foreach (var project in projects) wanted[Path.GetFullPath(project)] = null;
            foreach (var file in files.Select(f => Path.GetFullPath(f)))
            {
                if (ProjectOf(file) is not { } csproj)
                {
                    notChecked.Add($"{Path.GetFileName(file)}: no project compiles it");
                    continue;
                }
                if (!wanted.TryGetValue(csproj, out var only)) wanted[csproj] = [file];
                else only?.Add(file);
            }

            var built = new Dictionary<string, (CSharpCompilation Compilation, string Key)?>(StringComparer.OrdinalIgnoreCase);
            foreach (var (csproj, only) in wanted)
            {
                if (!File.Exists(csproj))
                {
                    notChecked.Add($"{Path.GetFileName(csproj)}: there is no such project");
                    continue;
                }
                if (!Checkable(csproj, notChecked) || Get(csproj, clock, budget, notChecked, cancellation) is not { } project) continue;
                if (Current(project, clock, budget, notChecked, cancellation, built) is not { } current) continue;

                var reported = project.Diagnostics(current.Compilation, current.Key, _loader, ids, cancellation);
                found.AddRange(only is null ? reported : reported.Where(d => only.Contains(d.FullPath)));
                notChecked.AddRange(project.LoadFailures.Select(failure => $"{project.Name}: {failure}"));
            }

            return new DiagnosticReport(found, Summarised(notChecked), clock.Elapsed);
        }
    }

    /// <summary>The project that compiles a file: the one project file in the nearest directory above it that has
    /// any. None for a file under <c>bin</c> or <c>obj</c>, or with two candidates side by side.</summary>
    public string? ProjectOf(string fullPath)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(fullPath));
        var walked = new List<string>();
        string? found = null;

        for (; dir is { Length: > 0 }; dir = Path.GetDirectoryName(dir))
        {
            if (_owners.TryGetValue(dir, out found)) break;
            walked.Add(dir);

            var name = Path.GetFileName(dir);
            if (name.Equals("bin", StringComparison.OrdinalIgnoreCase) || name.Equals("obj", StringComparison.OrdinalIgnoreCase))
            {
                found = null;
                break;
            }

            var projects = System.IO.Directory.Exists(dir) ? System.IO.Directory.GetFiles(dir, "*.csproj") : [];
            if (projects.Length > 0)
            {
                found = projects.Length == 1 ? projects[0] : null;
                break;
            }
            if (_boundary is not null && string.Equals(dir, _boundary, StringComparison.OrdinalIgnoreCase)) break;
        }

        foreach (var d in walked) _owners[d] = found;
        return found;
    }

    private CompiledProject? Get(string csproj, Stopwatch clock, TimeSpan budget, List<string> notChecked,
                                 CancellationToken cancellation)
    {
        var name = Path.GetFileNameWithoutExtension(csproj);

        if (_projects.TryGetValue(csproj, out var held))
        {
            if (held.Refresh(_loader, cancellation) is not { } stale) return held;
            _projects.Remove(csproj);
            notChecked.Add($"{name}: {stale}");
            return null;
        }

        if (clock.Elapsed > budget)
        {
            notChecked.Add(OverBudget + name);
            Warm(csproj);
            return null;
        }

        var (project, error) = CompiledProject.Load(csproj, _loader, cancellation);
        if (project is null)
        {
            notChecked.Add($"{name}: {error}");
            return null;
        }
        return _projects[csproj] = project;
    }

    /// <summary>
    /// Whether the compiler's errors for a project mean anything — not when its packages are not restored (see
    /// <see cref="CompiledProject.Unrestored"/>). Asked only where errors are reported: finding references needs no such
    /// trust, since a missing package does not stop this repository's own types binding, and refusing to load such a
    /// project there turned a reference search back into a search by spelling.
    /// </summary>
    private static bool Checkable(string csproj, List<string> notChecked)
    {
        if (CompiledProject.Unrestored(csproj) is not { } why) return true;
        notChecked.Add($"{Path.GetFileNameWithoutExtension(csproj)}: {why}");
        return false;
    }

    /// <summary>How a project the loading budget ran out on is marked, until <see cref="Summarised"/> folds them into one line.</summary>
    private const string OverBudget = "past budget: ";

    /// <summary>
    /// Loads a project the check ran out of budget for, after the check has answered — so the next one includes it rather
    /// than running out of budget at the same place again. Queued behind the lock, like any other use of the host.
    /// </summary>
    private void Warm(string csproj) => Task.Run(() =>
    {
        lock (_gate)
        {
            if (_projects.ContainsKey(csproj)) return;
            if (CompiledProject.Load(csproj, _loader, CancellationToken.None).Project is { } project) _projects[csproj] = project;
        }
    });

    /// <summary>What could not be checked, with the projects the budget ran out on said once rather than a line each.</summary>
    private static IReadOnlyList<string> Summarised(List<string> notChecked)
    {
        var late = notChecked.Where(n => n.StartsWith(OverBudget, StringComparison.Ordinal)).Select(n => n[OverBudget.Length..]).ToList();
        if (late.Count == 0) return notChecked;

        return [.. notChecked.Where(n => !n.StartsWith(OverBudget, StringComparison.Ordinal)),
                $"{late.Count} project(s) past the loading budget — {string.Join(", ", late)}. They are loading now, so the next "
              + "check includes them"];
    }

    /// <summary>
    /// The references of <paramref name="consumer"/> that are edited projects' assemblies, each swapped for that project's
    /// compilation — before-text for the before side, after-text for the after side. Without it a project that references
    /// another edited in the same change compiles against the assembly on disk, which has neither half of the change.
    /// </summary>
    private static (Dictionary<string, MetadataReference> Before, Dictionary<string, MetadataReference> After) Swaps(
        CompiledProject consumer, IEnumerable<(CompiledProject Project, CSharpCompilation Before, CSharpCompilation After)> edited)
    {
        var before = new Dictionary<string, MetadataReference>(StringComparer.OrdinalIgnoreCase);
        var after  = new Dictionary<string, MetadataReference>(StringComparer.OrdinalIgnoreCase);
        foreach (var (project, b, a) in edited)
            foreach (var output in consumer.ReferencesTo(project))
            {
                before[output] = b.ToMetadataReference();
                after[output]  = a.ToMetadataReference();
            }
        return (before, after);
    }

    /// <summary>
    /// A project's compilation as its sources stand, and a key for everything that compilation is a function of — with each
    /// reference to another project's build output that is missing, or older than that project's sources, swapped for a
    /// compilation of that project in turn. Null only for a project in a reference cycle, which is met while it is being built.
    /// <para>
    /// Without it a dependency edited and not yet rebuilt reads as it was before the edit, and one never built does not read
    /// at all — so every use of what it declares is an error on both sides of a check, and a line adding one more use
    /// "introduces" it. That is how an edit to a project, made straight after one to the project it uses, used to report
    /// errors that a build afterwards did not have.
    /// </para>
    /// </summary>
    private (CSharpCompilation Compilation, string Key)? Current(CompiledProject project, Stopwatch clock, TimeSpan budget,
                                                                 List<string> notChecked, CancellationToken cancellation,
                                                                 Dictionary<string, (CSharpCompilation Compilation, string Key)?> built)
    {
        if (built.TryGetValue(project.ProjectPath, out var held)) return held;
        built[project.ProjectPath] = null;   // being built: a cycle stops here rather than recursing for ever

        var swaps = Behind(project, clock, budget, notChecked, cancellation, built, out var dependencies);
        var current = (project.With(NoTexts, cancellation, swaps), $"{project.Fingerprint()}+{dependencies}");
        built[project.ProjectPath] = current;
        return current;
    }

    private static readonly IReadOnlyDictionary<string, string?> NoTexts = new Dictionary<string, string?>();

    /// <summary>The out-of-date references of <paramref name="project"/>, each swapped for a compilation of the project that
    /// builds it; <paramref name="key"/> is what those compilations are a function of.</summary>
    private Dictionary<string, MetadataReference> Behind(CompiledProject project, Stopwatch clock, TimeSpan budget,
                                                         List<string> notChecked, CancellationToken cancellation,
                                                         Dictionary<string, (CSharpCompilation Compilation, string Key)?> built,
                                                         out string key)
    {
        var swaps = new Dictionary<string, MetadataReference>(StringComparer.OrdinalIgnoreCase);
        var keys  = new List<string>();
        foreach (var output in project.Declared.Order(StringComparer.OrdinalIgnoreCase))
        {
            if (OwnerOfOutput(output) is not { } owner || !OutOfDate(output, owner)) continue;
            if (Get(owner, clock, budget, notChecked, cancellation) is not { } dependency) continue;
            if (Current(dependency, clock, budget, notChecked, cancellation, built) is not { } compiled) continue;

            swaps[output] = compiled.Compilation.ToMetadataReference();
            keys.Add(compiled.Key);
        }
        key = string.Join(',', keys);
        return swaps;
    }

    /// <summary>The project whose build writes <paramref name="output"/>: the one project file beside the <c>bin</c> or
    /// <c>obj</c> it is under. Null for anything else — a package, a framework reference.</summary>
    private static string? OwnerOfOutput(string output)
    {
        for (var dir = Path.GetDirectoryName(output); dir is { Length: > 0 }; dir = Path.GetDirectoryName(dir))
        {
            var name = Path.GetFileName(dir);
            if (!name.Equals("bin", StringComparison.OrdinalIgnoreCase) && !name.Equals("obj", StringComparison.OrdinalIgnoreCase)) continue;

            var parent = Path.GetDirectoryName(dir);
            return parent is not null && System.IO.Directory.Exists(parent) && System.IO.Directory.GetFiles(parent, "*.csproj") is [var only]
                ? only : null;
        }
        return null;
    }

    /// <summary>
    /// Whether a project's build output is missing, or older than its project file or any of its sources. A reference assembly
    /// is only rewritten when the project's public surface changes, so its age says nothing about a body edit — the
    /// intermediate assembly beside it, which every compile writes, is what is compared instead.
    /// </summary>
    private static bool OutOfDate(string output, string csproj)
    {
        var dir = Path.GetDirectoryName(output)!;
        var compiled = Path.GetFileName(dir) is "ref" or "refint" && Path.GetDirectoryName(dir) is { } above
            ? Path.Combine(above, Path.GetFileName(output))
            : output;
        if (!File.Exists(compiled)) return !File.Exists(output) || compiled == output;

        var written = File.GetLastWriteTimeUtc(compiled);
        if (File.GetLastWriteTimeUtc(csproj) > written) return true;

        var projectDir = Path.GetDirectoryName(csproj)!;
        try
        {
            return System.IO.Directory.EnumerateFiles(projectDir, "*.cs", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true })
                .Any(source => !IsBuildOutput(source, projectDir) && File.GetLastWriteTimeUtc(source) > written);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    private static bool IsBuildOutput(string path, string projectDir)
    {
        var relative = Path.GetRelativePath(projectDir, path);
        return relative.StartsWith("bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || relative.StartsWith("obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Out-of-date references first, then the edited projects' compilations over them.</summary>
    private static Dictionary<string, MetadataReference> Over(Dictionary<string, MetadataReference> behind,
                                                              Dictionary<string, MetadataReference> edited)
    {
        var merged = new Dictionary<string, MetadataReference>(behind, StringComparer.OrdinalIgnoreCase);
        foreach (var (output, reference) in edited) merged[output] = reference;
        return merged;
    }

    /// <summary>The edited projects in an order where each comes after the edited projects it references, so their
    /// compilations exist to be swapped in. A cycle, which no build could have produced either, is broken where it is met.</summary>
    private static IEnumerable<(CompiledProject Project, List<SourceChange> Changes)> DependenciesFirst(
        List<(CompiledProject Project, List<SourceChange> Changes)> projects)
    {
        var remaining = new List<(CompiledProject Project, List<SourceChange> Changes)>(projects);
        while (remaining.Count > 0)
        {
            var next = remaining.FirstOrDefault(r => remaining.All(o => ReferenceEquals(o.Project, r.Project)
                                                                     || r.Project.ReferencesTo(o.Project).Count == 0));
            if (next.Project is null) next = remaining[0];

            remaining.Remove(next);
            yield return next;
        }
    }

    /// <summary>After minus before and before minus after, matched by file, id and message — not by line, which every
    /// edit above an error moves.</summary>
    private static void Compare(IReadOnlyList<CompileProblem> before, IReadOnlyList<CompileProblem> after,
                                List<CompileProblem> introduced, List<CompileProblem> fixedOnes)
    {
        static (string, string, string) Key(CompileProblem p) => (p.FullPath.ToUpperInvariant(), p.Id, p.Message);

        var unmatched = before.GroupBy(Key).ToDictionary(g => g.Key, g => g.Count());
        foreach (var problem in after)
        {
            if (unmatched.TryGetValue(Key(problem), out var n) && n > 0) unmatched[Key(problem)] = n - 1;
            else introduced.Add(problem);
        }

        var remaining = after.GroupBy(Key).ToDictionary(g => g.Key, g => g.Count());
        foreach (var problem in before)
        {
            if (remaining.TryGetValue(Key(problem), out var n) && n > 0) remaining[Key(problem)] = n - 1;
            else fixedOnes.Add(problem);
        }
    }

    private static ISymbol? DeclaredAt(SemanticModel model, SyntaxTree tree, int position, CancellationToken cancellation)
    {
        var token = tree.GetRoot(cancellation).FindToken(position);
        for (var node = token.Parent; node is not null; node = node.Parent)
        {
            if (model.GetDeclaredSymbol(node, cancellation) is not { } symbol) continue;
            if (symbol.Name != token.ValueText && !(symbol is IMethodSymbol { MethodKind: MethodKind.Constructor }))
                continue;

            // A constructor is spelt with its type's name, and renaming one is renaming the type.
            return symbol is IMethodSymbol { MethodKind: MethodKind.Constructor or MethodKind.Destructor } structor
                ? structor.ContainingType
                : symbol;
        }
        return null;
    }

    private static void Scan(CSharpCompilation compilation, IEnumerable<string> paths, ISymbol target,
                             List<SymbolLocation> found, CancellationToken cancellation)
    {
        var wanted = paths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var name   = target.Name;

        foreach (var tree in compilation.SyntaxTrees.Where(t => wanted.Contains(t.FilePath)))
        {
            if (!tree.GetText(cancellation).ToString().Contains(name, StringComparison.Ordinal)) continue;

            var model = compilation.GetSemanticModel(tree);
            foreach (var token in tree.GetRoot(cancellation).DescendantTokens(descendIntoTrivia: true))
            {
                if (!token.IsKind(SyntaxKind.IdentifierToken) || token.ValueText != name || token.Parent is not { } node)
                    continue;

                var declared = model.GetDeclaredSymbol(node, cancellation);
                var info     = declared is null ? model.GetSymbolInfo(node, cancellation) : default;
                var symbols  = declared is not null ? ImmutableArray.Create(declared)
                             : info.Symbol is { } bound ? ImmutableArray.Create(bound)
                             : info.CandidateSymbols;

                if (!symbols.Any(s => Same(s, target) || Implements(s, target))) continue;

                var verbatim = token.Text.StartsWith('@') ? 1 : 0;
                found.Add(new SymbolLocation(tree.FilePath, token.Span.Start + verbatim, name.Length));
            }
        }
    }

    /// <summary>The same symbol, whichever compilation it was bound in — a consumer sees it through a reference,
    /// as a different object with the same documentation id and assembly.</summary>
    private static bool Same(ISymbol candidate, ISymbol target)
    {
        var symbol = candidate.OriginalDefinition;
        if (symbol is IMethodSymbol { ReducedFrom: { } unreduced }) symbol = unreduced;
        if (symbol is IMethodSymbol { MethodKind: MethodKind.Constructor or MethodKind.Destructor } && target is INamedTypeSymbol)
            symbol = symbol.ContainingType;
        if (symbol is IAliasSymbol) return false;

        return symbol.GetDocumentationCommentId() is { } id
            && id == target.GetDocumentationCommentId()
            && symbol.ContainingAssembly?.Name == target.ContainingAssembly?.Name;
    }

    /// <summary>Whether renaming <paramref name="target"/> has to rename <paramref name="candidate"/> too: an override
    /// of it, or an implementation of it as an interface member.</summary>
    private static bool Implements(ISymbol candidate, ISymbol target)
    {
        for (var over = Overridden(candidate); over is not null; over = Overridden(over))
            if (Same(over, target)) return true;

        if (candidate.ContainingType is not { } type) return false;
        return type.AllInterfaces.SelectMany(i => i.GetMembers(target.Name))
                   .Any(member => Same(member, target)
                               && SymbolEqualityComparer.Default.Equals(type.FindImplementationForInterfaceMember(member), candidate));

        static ISymbol? Overridden(ISymbol s) => s switch
        {
            IMethodSymbol m   => m.OverriddenMethod,
            IPropertySymbol p => p.OverriddenProperty,
            IEventSymbol e    => e.OverriddenEvent,
            _                 => null,
        };
    }
}
