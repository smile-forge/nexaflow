using System.Collections.Concurrent;
using Nexaflow.Services.Initiatives.Cli.Daemon;
using Nexaflow.Services.Initiatives.Graph;
using Nexaflow.Services.Initiatives.Graph.Model;
using Nexaflow.Syntax;
using Nexaflow.Syntax.Compiler;

namespace Nexaflow.Services.Initiatives.Cli;

/// <summary>
/// What an edit did beyond its own lines: which declarations it changed the outside of, what uses them, and what the
/// compiler says about the result — the questions a change is not finished without, answered as part of making it.
/// <para>
/// One <see cref="CompileHost"/> per code root, kept for the life of the process. In the resident process that is a
/// compilation held warm between edits, which is what makes asking the compiler cost a fraction of a second.
/// </para>
/// </summary>
internal static class EditCheck
{
    /// <summary>How long one check may spend loading projects it has not seen yet. A loaded project is always checked;
    /// past this the rest are named as not checked rather than waited for.</summary>
    internal static readonly TimeSpan LoadBudget = TimeSpan.FromSeconds(30);

    private const int ShownUses   = 10;
    private const int ShownErrors = 20;

    private static readonly ConcurrentDictionary<string, CompileHost> Hosts = new(StringComparer.OrdinalIgnoreCase);

    internal static CompileHost HostFor(string codeRoot) =>
        Hosts.GetOrAdd(Path.TrimEndingDirectorySeparator(Path.GetFullPath(codeRoot)), root => new CompileHost(root));

    /// <summary>Lets go of a tree's compiler, for a command stuck holding it: the next check on the tree loads afresh.</summary>
    internal static void Forget(string codeRoot) =>
        Hosts.TryRemove(Path.TrimEndingDirectorySeparator(Path.GetFullPath(codeRoot)), out _);

    /// <summary>
    /// The compiler as <c>ask</c>'s <c>diagnostics</c> stage asks it: repo-relative files and projects in, findings by
    /// repo-relative file out. A project is named as its file is (<c>Nexaflow.Features.Solver</c>), by the end of that name
    /// (<c>Features.Solver</c>) when only one ends so, or by its path.
    /// </summary>
    internal static GraphAsk.Diagnose DiagnoseIn(KnowledgeGraph graph, string codeRoot) => (files, projects, ids) =>
    {
        var notChecked = new List<string>();
        var paths      = new List<string>();
        foreach (var project in projects)
        {
            var (full, why) = ProjectFile(graph, codeRoot, project);
            if (full is not null) paths.Add(full);
            else notChecked.Add($"{project}: {why}");
        }

        var report = HostFor(codeRoot).Diagnostics([.. files.Select(f => Full(codeRoot, f))], paths, ids, LoadBudget,
                                                   RequestScope.Cancellation);
        return ([.. report.Found.Select(d => new GraphAsk.Finding(Relative(codeRoot, d.FullPath), d.Line, d.Id, d.Severity, d.Message))],
                report.Checked, [.. notChecked, .. report.NotChecked]);
    };

    /// <summary>The project file a name or path means, or why there is not exactly one.</summary>
    private static (string? Full, string Why) ProjectFile(KnowledgeGraph graph, string codeRoot, string project)
    {
        var wanted = project.Replace('\\', '/');
        if (wanted.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) && File.Exists(Full(codeRoot, wanted)))
            return (Full(codeRoot, wanted), "");

        // Only a .csproj has an extension to take off: Nexaflow.Features.Solver is a name, and "without extension" made it Nexaflow.Features.
        var name  = wanted.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ? Path.GetFileNameWithoutExtension(wanted) : Path.GetFileName(wanted);
        var known = graph.Nodes.Where(n => n.Type == NodeType.File && n.FilePath is { } p && p.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                               .Select(n => n.FilePath!)
                               .Distinct(StringComparer.OrdinalIgnoreCase)
                               .ToList();

        var exact = known.Where(p => Path.GetFileNameWithoutExtension(p).Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
        var named = exact.Count > 0 ? exact
            : known.Where(p => Path.GetFileNameWithoutExtension(p).EndsWith("." + name, StringComparison.OrdinalIgnoreCase)).ToList();

        if (named.Count == 0) return (null, "no project is called that");
        if (named.Count > 1)
            return (null, $"that could be {string.Join(" or ", named.Take(4).Select(Path.GetFileNameWithoutExtension))} - name one");
        return File.Exists(Full(codeRoot, named[0])) ? (Full(codeRoot, named[0]), "") : (null, "its project file is not in this tree");
    }

    /// <summary>One place a changed declaration is used.</summary>
    internal sealed record Use(string RelativePath, int Line, GraphNode? Owner);

    /// <param name="Changed">The names of the declarations whose outside changed — removed, renamed or re-signed.</param>
    /// <param name="Uses">What uses them, outside the declarations themselves.</param>
    /// <param name="ByCompiler">Whether <see cref="Uses"/> came from the compiler's binding. When it could not be asked
    /// they are the lines that spell the name, which is a superset.</param>
    /// <param name="Views">XAML lines naming them, which bind by text and so are never the compiler's to find.</param>
    /// <param name="Compile">The compiler's comparison of before and after, or null when no C# was involved.</param>
    internal sealed record Findings(IReadOnlyList<string> Changed, IReadOnlyList<Use> Uses, bool ByCompiler,
                                    IReadOnlyList<GraphMentions.Mention> Views, CompileReport? Compile,
                                    IReadOnlyList<string> UsesNotSearched);

    /// <summary>
    /// Works out what the plan's files change — on the planned text, before anything is written, so a dry run gets the
    /// same answer as the edit and <c>--must-compile</c> can refuse in time.
    /// </summary>
    internal static Findings Of(KnowledgeGraph graph, IReadOnlyList<EditPlan.Written> files, string codeRoot,
                                Func<string, string?> read)
    {
        var changed = new List<(EditPlan.Written File, string Grammar, StructuralEdit.Declaration Declaration)>();
        foreach (var file in files)
            if (TreeSitterLanguages.ForEdit(file.RelativePath) is { Length: > 0 } grammar)
                foreach (var declaration in StructuralEdit.ChangedDeclarations(grammar, file.Before ?? "", file.After ?? ""))
                    changed.Add((file, grammar, declaration));

        var names    = changed.Select(c => c.Declaration.Name).Distinct(StringComparer.Ordinal).ToList();
        var mentions = names.Count == 0 ? [] : GraphMentions.Of(graph, names, SourceFiles(codeRoot), read);
        var touched  = files.Select(f => f.RelativePath).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var host   = HostFor(codeRoot);
        var report = files.Any(f => IsCSharp(f.RelativePath) || f.RelativePath.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
            ? host.Check(
                [.. files.Where(f => IsCSharp(f.RelativePath) || f.RelativePath.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
                         .Select(f => new SourceChange(Full(codeRoot, f.RelativePath), f.Before, f.After))],
                [.. mentions.Where(m => IsCSharp(m.RelativePath) && !touched.Contains(m.RelativePath))
                            .Select(m => Full(codeRoot, m.RelativePath)).Distinct(StringComparer.OrdinalIgnoreCase)],
                LoadBudget, RequestScope.Cancellation)
            : null;

        // What uses each changed declaration, asked of the compiler where it can be: by binding, a Plan elsewhere that is
        // a different Plan is not a use. The file still holds its before-text — nothing is written until after this.
        var uses        = new List<Use>();
        var byCompiler  = true;
        var notSearched = new List<string>();
        foreach (var (file, grammar, declaration) in changed)
        {
            var candidates = mentions.Where(m => IsCSharp(m.RelativePath) && m.Text.Contains(declaration.Name, StringComparison.Ordinal))
                                     .Select(m => Full(codeRoot, m.RelativePath)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            if (grammar == "c-sharp" && file.Before is { } before
                && StructuralEdit.NameStartOf(grammar, before, declaration.AstPath, declaration.Name) is { } position
                && host.FindReferences(Full(codeRoot, file.RelativePath), position, candidates, LoadBudget, RequestScope.Cancellation)
                    is { Error: null } found)
            {
                notSearched.AddRange(found.NotChecked);
                foreach (var at in found.Locations)
                {
                    var rel = Relative(codeRoot, at.FullPath);
                    if (string.Equals(rel, file.RelativePath, StringComparison.OrdinalIgnoreCase) && at.Start == position) continue;

                    var text = string.Equals(rel, file.RelativePath, StringComparison.OrdinalIgnoreCase) ? before : read(rel);
                    var line = text is null ? 0 : text.AsSpan(0, Math.Min(at.Start, text.Length)).Count('\n') + 1;
                    uses.Add(new Use(rel, line, GraphMentions.OwnerOf(graph, rel, line)));
                }
                continue;
            }

            byCompiler = false;
            uses.AddRange(mentions.Where(m => !m.RelativePath.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase)
                                           && m.Text.Contains(declaration.Name, StringComparison.Ordinal)
                                           && !touched.Contains(m.RelativePath))
                                  .Select(m => new Use(m.RelativePath, m.Line, m.Owner)));
        }

        var views = mentions.Where(m => m.RelativePath.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase)).ToList();
        return new Findings(names, uses.Distinct().ToList(), byCompiler, views, report, notSearched.Distinct().ToList());
    }

    internal static void Print(Findings findings, string codeRoot)
    {
        if (findings.Changed.Count > 0)
        {
            var names  = string.Join(", ", findings.Changed.Take(6)) + (findings.Changed.Count > 6 ? ", …" : "");
            var owners = findings.Uses.GroupBy(u => u.Owner?.Id ?? $"file:{u.RelativePath}", StringComparer.Ordinal).ToList();
            var files  = findings.Uses.Select(u => u.RelativePath).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            var how    = findings.ByCompiler ? "" : " (by spelling — the compiler could not be asked, so some may be another thing of the same name)";

            Console.WriteLine(owners.Count == 0
                ? $"impact: {names} changed from outside, and nothing else uses {(findings.Changed.Count == 1 ? "it" : "them")}{how}."
                : $"impact: {names} changed from outside — used by {owners.Count} declaration(s) in {files} file(s){how}:");
            foreach (var owner in owners.Take(ShownUses))
                Console.WriteLine($"  {owner.Key}   line {owner.First().Line}" + (owner.Count() > 1 ? $" (+{owner.Count() - 1})" : ""));
            if (owners.Count > ShownUses) Console.WriteLine($"  … and {owners.Count - ShownUses} more");

            if (findings.Views.Count > 0)
                Console.WriteLine($"impact: {findings.Views.Count} XAML line(s) name it as text, which no compiler checks: "
                                + string.Join(", ", findings.Views.Take(4).Select(v => $"{v.RelativePath}:{v.Line}"))
                                + (findings.Views.Count > 4 ? ", …" : ""));
            foreach (var reason in findings.UsesNotSearched) Console.WriteLine($"impact: not searched — {reason}");
        }

        if (findings.Compile is not { } report) return;

        // One verdict per check whatever it found, carrying what it covered — how many files, which projects, and whether they
        // were already loaded — so a clean one can be taken at its word without a build. What is wrong is listed beneath it.
        var took = report.Elapsed.TotalSeconds < 1 ? $"{report.Elapsed.TotalSeconds:F2}s" : $"{report.Elapsed.TotalSeconds:F1}s";
        var how_ = report.Loaded is { Count: > 0 } loaded ? $"{took}, loaded {string.Join(", ", loaded)}" : $"{took}, all warm";

        if (report.Checked.Count > 0)
        {
            Console.WriteLine((report.Introduced.Count == 0 ? "compile: no new errors" : $"compile: {report.Introduced.Count} new error(s)")
                            + $" in {report.Files} file(s) — {string.Join(", ", report.Checked)} ({how_})"
                            + (report.Standing > 0 ? $"; {report.Standing} error(s) were there already" : ""));
            foreach (var problem in report.Introduced.Take(ShownErrors))
                Console.WriteLine($"  {Relative(codeRoot, problem.FullPath)}:{problem.Line}:{problem.Column}  {problem.Id}  {problem.Message}");
            if (report.Introduced.Count > ShownErrors) Console.WriteLine($"  … and {report.Introduced.Count - ShownErrors} more");
            if (report.Fixed.Count > 0)
                Console.WriteLine($"compile: fixed {report.Fixed.Count} error(s) — {string.Join(", ", report.Fixed.Select(p => p.Id).Distinct())}");
        }

        if (report.Views is { } views)
        {
            Console.WriteLine((views.Introduced.Count == 0 ? "views: no new analyzer findings" : $"views: {views.Introduced.Count} new analyzer finding(s)")
                            + $" in {string.Join(", ", views.Checked.Select(v => Relative(codeRoot, v)))} ({how_})"
                            + (views.Standing > 0 ? $"; {views.Standing} were there already" : "")
                            + " — the markup itself is compiled only by a build");
            foreach (var problem in views.Introduced.Take(ShownErrors))
                Console.WriteLine($"  {Relative(codeRoot, problem.FullPath)}:{problem.Line}:{problem.Column}  {problem.Id}  {problem.Message}"
                                + $"   --at {problem.Line}:{problem.Column}");
            if (views.Fixed.Count > 0)
                Console.WriteLine($"views: fixed {views.Fixed.Count} — {string.Join(", ", views.Fixed.Select(p => p.Id).Distinct())}");
        }

        foreach (var reason in report.NotChecked) Console.WriteLine($"compile: not checked — {reason}");
    }

    /// <summary>Directories no source this repository compiles lives in: build output, other people's code, tooling state.</summary>
    private static readonly HashSet<string> NotSource = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", "external", "node_modules", "packages", "test-samples", ".git", ".vs", ".claude", ".product",
    };

    /// <summary>
    /// The C# and XAML files under <paramref name="codeRoot"/>, repo-relative, read from disk rather than from the graph so
    /// a file created a moment ago is among them. A few thousand directory entries, which costs less than a parse.
    /// </summary>
    internal static IEnumerable<string> SourceFiles(string codeRoot)
    {
        var pending = new Stack<string>([codeRoot]);
        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            IEnumerable<string> entries;
            try { entries = Directory.EnumerateFileSystemEntries(dir); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }

            foreach (var entry in entries)
            {
                var name = Path.GetFileName(entry);
                if (Directory.Exists(entry))
                {
                    if (!NotSource.Contains(name)) pending.Push(entry);
                }
                else if (name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
                    yield return Relative(codeRoot, entry);
            }
        }
    }

    internal static string Full(string codeRoot, string rel) => Path.Combine(codeRoot, rel.Replace('/', Path.DirectorySeparatorChar));

    internal static string Relative(string codeRoot, string full) => Path.GetRelativePath(codeRoot, full).Replace('\\', '/');

    private static bool IsCSharp(string path) => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase);
}