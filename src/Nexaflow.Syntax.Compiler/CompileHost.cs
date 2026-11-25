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

/// <summary>
/// What a project's compiler and analyzers report. <c>Checked</c> names each project answered for and what answered — so a
/// clean report says what it is clean by — and <c>NotChecked</c> each that could not be, and why.
/// </summary>
public sealed record DiagnosticReport(IReadOnlyList<CompileDiagnostic> Found, IReadOnlyList<string> Checked,
                                      IReadOnlyList<string> NotChecked, TimeSpan Elapsed);

/// <summary>What an edit did to the build.</summary>
/// <param name="Checked">The projects compiled for the answer.</param>
/// <param name="Introduced">Errors after the edit that were not there before it.</param>
/// <param name="Fixed">Errors before the edit that are gone after it.</param>
/// <param name="NotChecked">What could not be checked, and why — so a clean answer is never mistaken for a
/// complete one.</param>
/// <param name="Files">The files whose errors were compared: what a clean answer covered.</param>
/// <param name="Standing">Errors in those files both before and after — there already, so a clean answer does not read as
/// clean code.</param>
/// <param name="Loaded">The projects this check had to load rather than found warm — what a slow one was slow on.</param>
/// <param name="Views">What the analyzers said of the views the edit changed, when it changed any.</param>
public sealed record CompileReport(IReadOnlyList<string> Checked, IReadOnlyList<CompileProblem> Introduced,
                                   IReadOnlyList<CompileProblem> Fixed, IReadOnlyList<string> NotChecked, TimeSpan Elapsed,
                                   int Files = 0, int Standing = 0, IReadOnlyList<string>? Loaded = null,
                                   ViewReport? Views = null);

/// <summary>
/// A view's check: its markup is compiled only by a build, but the analyzers handed it can be asked about it before and after
/// an edit, the way C# is compiled before and after.
/// </summary>
/// <param name="Checked">The views asked about, by full path.</param>
/// <param name="Standing">Findings there both before and after.</param>
public sealed record ViewReport(IReadOnlyList<string> Checked, IReadOnlyList<CompileProblem> Introduced,
                                IReadOnlyList<CompileProblem> Fixed, int Standing);

/// <summary>Where a symbol is declared and used, by file and character span.</summary>
public sealed record SymbolLocation(string FullPath, int Start, int Length);

/// <param name="Symbol">The symbol found at the position asked about, as the compiler names it — null when there
/// was none, with <paramref name="Error"/> saying why.</param>
/// <param name="Unsearched">Candidate files whose project could not be searched, so a caller that must not miss a use
/// can fall back to something coarser for those.</param>
public sealed record ReferenceReport(string? Symbol, IReadOnlyList<SymbolLocation> Locations, IReadOnlyList<string> NotChecked,
                                     string? Error, IReadOnlyList<string>? Unsearched = null);

/// <summary>One declaration to find the uses of: the file it is in, the position of its name, and the files outside its
/// own project that could use it — those that spell its name.</summary>
public sealed record ReferenceQuery(string DeclarationFile, int Position, IReadOnlyCollection<string> CandidateFiles);

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
    private readonly IDesignTimeBuilder _designTime;

    /// <summary>The projects the check in progress had to load rather than found warm.</summary>
    private readonly List<string> _loadedNow = [];

    /// <param name="boundary">The directory a search for a file's project stops at — the repository, so a temp file is
    /// never claimed by a project somewhere above it.</param>
    /// <param name="designTime">Who runs the design-time build that tells a project's compiler arguments: a host keeping
    /// MSBuild workers passes its own, and without one each runs in an MSBuild of its own.</param>
    public CompileHost(string? boundary = null, IDesignTimeBuilder? designTime = null)
    {
        _boundary   = boundary is { Length: > 0 } ? Path.TrimEndingDirectorySeparator(Path.GetFullPath(boundary)) : null;
        _designTime = designTime ?? ProcessDesignTimeBuilder.Instance;
    }

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
            var notChecked = new List<Unchecked>();

            var files    = 0;
            var standing = 0;
            _loadedNow.Clear();

            // A view is not compiled here - its generated code comes from the build - but the analyzers it is handed can be asked,
            // before and after, as C# is compiled before and after.
            var viewsChecked    = new List<string>();
            var viewsIntroduced = new List<CompileProblem>();
            var viewsFixed      = new List<CompileProblem>();
            var viewsStanding   = 0;
            foreach (var change in changes.Where(c => c.FullPath.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase)))
            {
                if (ProjectOf(change.FullPath) is not { } owner)
                {
                    notChecked.Add(new(Path.GetFileName(change.FullPath), "no project compiles it"));
                    continue;
                }
                if (!Checkable(owner, notChecked) || Get(owner, clock, budget, notChecked, cancellation) is not { } project) continue;

                if (project.ViewFindings(change.FullPath, change.Before, _loader, cancellation) is not { } was)
                {
                    notChecked.Add(new(Path.GetFileName(change.FullPath), $"{project.Name} hands it to no analyzer"));
                    continue;
                }
                var now = project.ViewFindings(change.FullPath, change.After, _loader, cancellation) ?? [];
                viewsStanding += Compare(was, now, viewsIntroduced, viewsFixed);
                notChecked.AddRange(project.LoadFailures.Select(failure => new Unchecked(project.Name, failure)));
                viewsChecked.Add(change.FullPath);
            }

            var byProject = new Dictionary<string, List<SourceChange>>(StringComparer.OrdinalIgnoreCase);
            foreach (var change in changes.Where(c => c.FullPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)))
            {
                if (ProjectOf(change.FullPath) is not { } csproj)
                {
                    notChecked.Add(new(Path.GetFileName(change.FullPath), "no project compiles it"));
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

                standing += Compare(CompiledProject.ErrorsIn(before, targets, cancellation),
                                    CompiledProject.ErrorsIn(after, targets, cancellation), introduced, fixedOnes);
                files    += targets.Count;
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
                standing += Compare(CompiledProject.ErrorsIn(consumer.With(NoTexts, cancellation, Over(behind, before)), targets, cancellation),
                                    CompiledProject.ErrorsIn(consumer.With(NoTexts, cancellation, Over(behind, after)), targets, cancellation),
                                    introduced, fixedOnes);
                files    += targets.Count;
                checkedOn.Add(consumer.Name);
            }

            return new CompileReport(checkedOn, introduced, fixedOnes, Summarised(notChecked), clock.Elapsed, files, standing, [.. _loadedNow],
                                     viewsChecked.Count == 0 ? null : new ViewReport(viewsChecked, viewsIntroduced, viewsFixed, viewsStanding));
        }
    }

    /// <summary>
    /// Every declaration of, and reference to, the symbol declared at <paramref name="position"/> in
    /// <paramref name="declarationFile"/>: in its own project, and in <paramref name="candidateFiles"/> elsewhere whose
    /// project compiles against it. Found by the compiler's binding rather than by spelling, so a different member that
    /// happens to share the name is left alone — and overrides and interface implementations of it are included.
    /// </summary>
    public ReferenceReport FindReferences(string declarationFile, int position, IReadOnlyCollection<string> candidateFiles,
                                          TimeSpan budget, CancellationToken cancellation = default) =>
        FindReferences([new ReferenceQuery(declarationFile, position, candidateFiles)], budget, cancellation)[0];

    /// <summary>
    /// The uses of several declarations at once, one report for each query in order. One pass however many there are: each
    /// consuming project is compiled once with every declaring project it references swapped in, and each candidate file is
    /// bound once against every name it could hold — where a query at a time compiled each consumer afresh for every one,
    /// throwing away all it had bound, and read the same files again.
    /// </summary>
    public IReadOnlyList<ReferenceReport> FindReferences(IReadOnlyList<ReferenceQuery> queries, TimeSpan budget,
                                                         CancellationToken cancellation = default)
    {
        lock (_gate)
        {
            var clock      = Stopwatch.StartNew();
            var notChecked = new List<Unchecked>();
            var none       = new Dictionary<string, string?>();
            var errors     = new string?[queries.Count];
            var symbols    = new ISymbol?[queries.Count];
            var found      = queries.Select(_ => new List<SymbolLocation>()).ToArray();
            var unsearched = queries.Select(_ => new List<string>()).ToArray();
            var declaring  = new Dictionary<string, (CompiledProject Project, CSharpCompilation Compilation, List<int> Queries)>(
                                 StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < queries.Count; i++)
            {
                var query = queries[i];
                if (ProjectOf(query.DeclarationFile) is not { } csproj)
                {
                    errors[i] = $"no project compiles {Path.GetFileName(query.DeclarationFile)}";
                    continue;
                }
                if (!declaring.TryGetValue(csproj, out var held))
                {
                    if (Get(csproj, clock, budget, notChecked, cancellation) is not { } project)
                    {
                        errors[i] = string.Join("; ", Summarised(notChecked));
                        continue;
                    }
                    declaring[csproj] = held = (project, project.With(none, cancellation), []);
                }

                var tree = held.Compilation.SyntaxTrees.FirstOrDefault(t => string.Equals(t.FilePath, query.DeclarationFile, StringComparison.OrdinalIgnoreCase));
                if (tree is null)
                    errors[i] = $"{held.Project.Name} does not compile {Path.GetFileName(query.DeclarationFile)}";
                else if (DeclaredAt(held.Compilation.GetSemanticModel(tree), tree, query.Position, cancellation) is not { } symbol)
                    errors[i] = "nothing is declared at that position";
                else
                {
                    symbols[i] = symbol;
                    held.Queries.Add(i);
                }
            }

            // Within its own project a declaration is looked for everywhere, as the compiler sees it.
            foreach (var (_, (project, compilation, asked)) in declaring)
                Scan(compilation, project.SourcePaths, [.. asked.Select(i => (symbols[i]!, i))], (_, _) => true, found, cancellation);

            // Elsewhere only in the files that could name it: each consumer once, with every declaring project it references
            // swapped in for its built output, so a use binds to the declaration as it stands in the tree.
            var candidates = queries.Select(q => q.CandidateFiles.ToHashSet(StringComparer.OrdinalIgnoreCase)).ToArray();
            foreach (var group in queries.SelectMany(q => q.CandidateFiles).Distinct(StringComparer.OrdinalIgnoreCase)
                                         .GroupBy(f => ProjectOf(f) ?? "", StringComparer.OrdinalIgnoreCase))
            {
                if (group.Key.Length == 0) continue;
                var interested = Enumerable.Range(0, queries.Count)
                                           .Where(i => symbols[i] is not null
                                                    && !string.Equals(ProjectOf(queries[i].DeclarationFile), group.Key, StringComparison.OrdinalIgnoreCase)
                                                    && group.Any(candidates[i].Contains))
                                           .ToList();
                if (interested.Count == 0) continue;

                if (Get(group.Key, clock, budget, notChecked, cancellation) is not { } consumer)
                {
                    foreach (var i in interested) unsearched[i].AddRange(group.Where(candidates[i].Contains));
                    continue;
                }

                var swaps     = new Dictionary<string, MetadataReference>(StringComparer.OrdinalIgnoreCase);
                var reachable = new List<(ISymbol, int)>();
                foreach (var (_, (project, compilation, asked)) in declaring)
                {
                    var outputs = consumer.ReferencesTo(project);
                    if (outputs.Count == 0) continue;
                    foreach (var output in outputs) swaps[output] = compilation.ToMetadataReference();
                    reachable.AddRange(asked.Where(interested.Contains).Select(i => (symbols[i]!, i)));
                }
                if (reachable.Count == 0) continue;

                Scan(consumer.With(none, cancellation, swaps), group, reachable, (i, file) => candidates[i].Contains(file), found, cancellation);
            }

            var summary = Summarised(notChecked);
            return [.. Enumerable.Range(0, queries.Count).Select(i => errors[i] is { } error
                ? new ReferenceReport(null, [], summary, error, unsearched[i])
                : new ReferenceReport(symbols[i]!.ToDisplayString(), [.. found[i].Distinct()], summary, null, unsearched[i]))];
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
            var notChecked = new List<Unchecked>();
            var checkedBy  = new List<string>();
            var found      = new List<CompileDiagnostic>();

            // Each project, with the files of it that were asked about - null for all of it.
            var wanted = new Dictionary<string, HashSet<string>?>(StringComparer.OrdinalIgnoreCase);
            foreach (var project in projects) wanted[Path.GetFullPath(project)] = null;
            foreach (var file in files.Select(f => Path.GetFullPath(f)))
            {
                if (ProjectOf(file) is not { } csproj)
                {
                    notChecked.Add(new(Path.GetFileName(file), "no project compiles it"));
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
                    notChecked.Add(new(Path.GetFileName(csproj), "there is no such project"));
                    continue;
                }
                if (!Checkable(csproj, notChecked) || Get(csproj, clock, budget, notChecked, cancellation) is not { } project) continue;
                if (Current(project, clock, budget, notChecked, cancellation, built) is not { } current) continue;

                var reported = project.Diagnostics(current.Compilation, current.Key, _loader, ids, cancellation);
                found.AddRange(only is null ? reported : reported.Where(d => only.Contains(d.FullPath)));
                notChecked.AddRange(project.LoadFailures.Select(failure => new Unchecked(project.Name, failure)));

                // A clean answer is only as good as what could have reported: name that, and when nothing could, it is no answer.
                if (project.ReportersOf(_loader, ids) is not { Length: > 0 } reporters)
                    notChecked.Add(new(project.Name, $"nothing it runs reports /{ids}/ - neither the compiler nor any analyzer it loads declares that id"));
                else
                    checkedBy.Add($"{project.Name} ({(only is null ? project.Coverage : $"{only.Count} of its files")}) by {reporters}");
            }

            return new DiagnosticReport(found, checkedBy, Summarised(notChecked), clock.Elapsed);
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

    private CompiledProject? Get(string csproj, Stopwatch clock, TimeSpan budget, List<Unchecked> notChecked,
                                 CancellationToken cancellation)
    {
        var name = Path.GetFileNameWithoutExtension(csproj);

        if (_projects.TryGetValue(csproj, out var held))
        {
            if (held.Refresh(_loader, cancellation) is not { } stale) return held;
            _projects.Remove(csproj);
            notChecked.Add(new(name, stale));
            return null;
        }

        if (clock.Elapsed > budget)
        {
            notChecked.Add(new(name, PastBudget));
            Warm(csproj);
            return null;
        }

        var (project, error) = CompiledProject.Load(csproj, _loader, _designTime, cancellation);
        if (project is null)
        {
            notChecked.Add(new(name, error ?? "it would not load, and gave no reason"));
            return null;
        }
        _loadedNow.Add(name);
        return _projects[csproj] = project;
    }

    /// <summary>
    /// Whether the compiler's errors for a project mean anything — not when its packages are not restored (see
    /// <see cref="CompiledProject.Unrestored"/>). Asked only where errors are reported: finding references needs no such
    /// trust, since a missing package does not stop this repository's own types binding, and refusing to load such a
    /// project there turned a reference search back into a search by spelling.
    /// </summary>
    private static bool Checkable(string csproj, List<Unchecked> notChecked)
    {
        if (CompiledProject.Unrestored(csproj) is not { } why) return true;
        notChecked.Add(new(Path.GetFileNameWithoutExtension(csproj), why));
        return false;
    }

    /// <summary>Why a project the loading budget ran out on was not checked.</summary>
    private const string PastBudget = "past the loading budget - loading now, so the next check covers it";

    /// <summary>How many of the things sharing one reason are named in the line that gives it.</summary>
    private const int NamedPerReason = 6;

    /// <summary>One thing a check could not cover, and why: kept apart so that a reason many share is said once, naming them
    /// (see <see cref="Summarised"/>).</summary>
    private readonly record struct Unchecked(string What, string Why);

    /// <summary>
    /// Loads a project the check ran out of budget for, after the check has answered — so the next one includes it rather
    /// than running out of budget at the same place again. Queued behind the lock, like any other use of the host.
    /// </summary>
    private void Warm(string csproj) => Task.Run(() =>
    {
        lock (_gate)
        {
            if (_projects.ContainsKey(csproj)) return;
            if (CompiledProject.Load(csproj, _loader, _designTime, CancellationToken.None).Project is { } project) _projects[csproj] = project;
        }
    });

    /// <summary>
    /// What could not be checked, one line per reason. A reason one thing has is said of it; a reason several share is said
    /// once, naming the first <see cref="NamedPerReason"/> - a fresh worktree has dozens of projects with nothing restored,
    /// and dozens of lines saying so bury the one that says something else.
    /// </summary>
    private static IReadOnlyList<string> Summarised(List<Unchecked> notChecked) =>
    [
        .. notChecked.Distinct().GroupBy(n => n.Why, StringComparer.Ordinal).Select(reason =>
        {
            var what = reason.Select(n => n.What).ToList();
            if (what.Count == 1) return $"{what[0]}: {reason.Key}";
            var named = string.Join(", ", what.Take(NamedPerReason)) + (what.Count > NamedPerReason ? $" and {what.Count - NamedPerReason} more" : "");
            return $"{what.Count} - {named}: {reason.Key}";
        }),
    ];

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
                                                                 List<Unchecked> notChecked, CancellationToken cancellation,
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
                                                         List<Unchecked> notChecked, CancellationToken cancellation,
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

    /// <summary>
    /// Pairs the problems before an edit with those after it, by file, id and message, into what the edit introduced and what
    /// it fixed. Returns how many were there both times.
    /// </summary>
    private static int Compare(IReadOnlyList<CompileProblem> before, IReadOnlyList<CompileProblem> after,
                               List<CompileProblem> introduced, List<CompileProblem> fixedOnes)
    {
        static (string, string, string) Key(CompileProblem p) => (p.FullPath.ToUpperInvariant(), p.Id, p.Message);

        var was       = introduced.Count;
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

        return after.Count - (introduced.Count - was);
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

    /// <summary>
    /// Every use of the <paramref name="targets"/> in <paramref name="paths"/>, each file read and bound once for all of them:
    /// only identifiers spelled like one are bound, and each is compared with the targets of that name. A use is recorded for
    /// a target only where <paramref name="wanted"/> says that file was asked about for it.
    /// </summary>
    private static void Scan(CSharpCompilation compilation, IEnumerable<string> paths, IReadOnlyList<(ISymbol Symbol, int Query)> targets,
                             Func<int, string, bool> wanted, List<SymbolLocation>[] found, CancellationToken cancellation)
    {
        var files  = paths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var byName = targets.GroupBy(t => t.Symbol.Name, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        foreach (var tree in compilation.SyntaxTrees.Where(t => files.Contains(t.FilePath)))
        {
            var text = tree.GetText(cancellation).ToString();
            if (!byName.Keys.Any(name => text.Contains(name, StringComparison.Ordinal))) continue;

            SemanticModel? model = null;
            foreach (var token in tree.GetRoot(cancellation).DescendantTokens(descendIntoTrivia: true))
            {
                if (!token.IsKind(SyntaxKind.IdentifierToken) || !byName.TryGetValue(token.ValueText, out var named) || token.Parent is not { } node)
                    continue;

                model ??= compilation.GetSemanticModel(tree);
                var declared = model.GetDeclaredSymbol(node, cancellation);
                var info     = declared is null ? model.GetSymbolInfo(node, cancellation) : default;
                var symbols  = declared is not null ? ImmutableArray.Create(declared)
                             : info.Symbol is { } bound ? ImmutableArray.Create(bound)
                             : info.CandidateSymbols;

                foreach (var (target, query) in named)
                {
                    if (!wanted(query, tree.FilePath) || !symbols.Any(s => Same(s, target) || Implements(s, target))) continue;
                    var verbatim = token.Text.StartsWith('@') ? 1 : 0;
                    found[query].Add(new SymbolLocation(tree.FilePath, token.Span.Start + verbatim, target.Name.Length));
                }
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
