using System.Collections.Immutable;
using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Nexaflow.Syntax.Compiler;

/// <summary>A source file as an edit found it and as it leaves it — null for a file that is not there.</summary>
public sealed record SourceChange(string FullPath, string? Before, string? After);

/// <summary>One compiler error, where it is.</summary>
public sealed record CompileProblem(string FullPath, int Line, int Column, string Id, string Message);

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
                if (Get(csproj, clock, budget, notChecked, cancellation) is { } project) loaded.Add((project, list));

            var edited = new List<(CompiledProject Project, CSharpCompilation Before, CSharpCompilation After)>();
            foreach (var (project, list) in DependenciesFirst(loaded))
            {
                var (swapBefore, swapAfter) = Swaps(project, edited);
                var before  = project.With(list.ToDictionary(c => c.FullPath, c => c.Before, StringComparer.OrdinalIgnoreCase), cancellation, swapBefore);
                var after   = project.With(list.ToDictionary(c => c.FullPath, c => c.After, StringComparer.OrdinalIgnoreCase), cancellation, swapAfter);
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
                if (Get(group.Key, clock, budget, notChecked, cancellation) is not { } consumer) continue;

                // Only a project that compiles against an edited one can be broken by it; one that merely mentions a
                // name of the same spelling is about something else.
                var (before, after) = Swaps(consumer, edited);
                if (before.Count == 0) continue;

                var none    = new Dictionary<string, string?>();
                var targets = group.ToHashSet(StringComparer.OrdinalIgnoreCase);
                Compare(CompiledProject.ErrorsIn(consumer.With(none, cancellation, before), targets, cancellation),
                        CompiledProject.ErrorsIn(consumer.With(none, cancellation, after), targets, cancellation),
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
