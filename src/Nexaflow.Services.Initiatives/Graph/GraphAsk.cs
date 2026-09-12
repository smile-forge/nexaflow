using System.Text;
using System.Text.RegularExpressions;
using Nexaflow.Services.Initiatives.Graph.Model;

namespace Nexaflow.Services.Initiatives.Graph;

/// <summary>
/// One question asked in several steps, answered once.
/// <para>
/// Every other verb answers exactly one thing, so the questions actually worth asking — "find this and show
/// me it", "who calls this, and what do their call sites look like", "does anything still do X, and in which
/// files" — cost three or four calls, each re-printing its own headers and each costing the caller a whole
/// turn to read. That is the reason a repo-wide search gets abandoned for a blanket grep: not that the graph
/// cannot answer, but that its answer arrives in instalments. A pipeline is those calls without the
/// instalments: stages are separated by <c>|</c>, the set of nodes flows left to right, and only the last
/// stage prints.
/// </para>
/// <para>
/// A vocabulary rather than a flag set, deliberately. A flag has to be added to every verb it could apply to
/// and combines with nothing else, so the surface grows faster than what it can express — and a surface too
/// wide to hold in mind is one that gets ignored in favour of the blunt tool. A stage is written once and
/// composes with every stage that already exists: <c>callers</c> was written knowing nothing about
/// <c>source</c>, and <c>callers | source</c> works anyway.
/// </para>
/// <para>
/// Pure: a loaded graph and a callback for reading source in, text out — so the CLI and the in-app assistant
/// can ask the same question and be told the same thing, exactly as with <see cref="GraphQuery"/>.
/// </para>
/// </summary>
public static class GraphAsk
{
    /// <summary>What the answer said, and whether the question was one this vocabulary could read.</summary>
    public sealed record Answer(string Text, bool Ok);

    /// <summary>Printed with every refusal, because the whole vocabulary is shorter than an explanation of
    /// which part of it was wrong.</summary>
    public const string Vocabulary = """
        start:  search <term> | grep <regex> | node <id>[,<id>...]
        narrow: callers | callees | members | like <regex> | limit <n>
        print:  ids [n] | source [n] | files | count            (ids, when the question says nothing)
        """;

    private const int ShownIds = 40;
    private const int ShownSource = 8;
    private const int SourceLines = 240;
    private const int LinesPerNode = 3;

    /// <summary>A node, and whatever lines of it a <c>grep</c> stage matched on the way past.</summary>
    private sealed record Hit(GraphNode Node, List<(int Line, string Text)> Lines);

    private sealed class Question
    {
        public List<Hit> Hits = [];
        public bool Seeded;
        public string Sink = "ids";
        public int Room;
    }

    /// <summary>
    /// Answers every question in <paramref name="script"/> — one per line, so a pattern may contain a
    /// semicolon and a <c>#</c> line is a comment. Ok is false if any of them could not be read, which is
    /// what the CLI turns into an exit code.
    /// </summary>
    public static Answer Run(KnowledgeGraph graph, string script, GraphQuery.ReadLines read)
    {
        var questions = script.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .ToList();
        if (questions.Count == 0) return new Answer($"ask: no question.\n{Vocabulary}", false);

        var sb = new StringBuilder();
        var ok = true;
        foreach (var question in questions)
        {
            if (questions.Count > 1) sb.AppendLine($"-- {question}");
            var answer = One(graph, question, read);
            sb.AppendLine(answer.Text);
            ok &= answer.Ok;
        }
        return new Answer(sb.ToString().TrimEnd(), ok);
    }

    private static Answer One(KnowledgeGraph g, string question, GraphQuery.ReadLines read)
    {
        if (!Split(question, out var stages, out var bad)) return new Answer($"ask: {bad}\n{Vocabulary}", false);

        var q = new Question();
        for (var i = 0; i < stages.Count; i++)
            if (Stage(g, read, q, stages[i], last: i == stages.Count - 1) is { } error)
                return new Answer($"ask: {error}\n{Vocabulary}", false);

        return new Answer(Print(g, q, read), true);
    }

    // -- Reading the question ---------------------------------------------------

    /// <summary>
    /// Stages, then words. Quotes are honoured before <c>|</c> is looked for, because a regex is the most
    /// likely thing to carry one: <c>grep "Foo|Bar"</c> is one stage, and an unquoted alternation would
    /// otherwise be silently read as two.
    /// </summary>
    private static bool Split(string text, out List<List<string>> stages, out string? error)
    {
        stages = [];
        error = null;

        // `stages` is an out parameter, and a local function cannot capture one - so the stages are gathered
        // here and handed over at the end.
        var found = new List<List<string>>();
        var words = new List<string>();
        var word = new StringBuilder();
        var open = '\0';
        var started = false;

        void EndWord()
        {
            if (!started) return;
            words.Add(word.ToString());
            word.Clear();
            started = false;
        }

        void EndStage()
        {
            EndWord();
            found.Add(words);
            words = [];
        }

        foreach (var ch in text)
        {
            if (open != '\0')
            {
                if (ch == open) open = '\0';
                else word.Append(ch);
                started = true;
                continue;
            }

            if (ch is '\'' or '"') { open = ch; started = true; continue; }
            if (ch == '|') { EndStage(); continue; }
            if (char.IsWhiteSpace(ch)) { EndWord(); continue; }
            word.Append(ch);
            started = true;
        }
        EndStage();

        if (open != '\0') { error = "a quote is left open."; return false; }
        if (found.Exists(s => s.Count == 0))
        {
            error = "an empty stage - two | with nothing between them, or a | at one end. "
                  + "A regex holding a | has to be quoted.";
            return false;
            }
            stages = found;
            return true;
    }

    private static string? Stage(KnowledgeGraph g, GraphQuery.ReadLines read, Question q, List<string> words, bool last)
    {
        var name = words[0].ToLowerInvariant();
        var rest = words.Skip(1).ToList();
        var arg = rest.Count > 0 ? string.Join(' ', rest) : null;

        switch (name)
        {
            case "search":
                if (arg is null) return "search needs a term: search <term>.";
                if (q.Seeded) return "search starts a question, so it cannot follow a |.";
                return Seed(q, GraphQuery.Search(g, arg));

            case "node":
            {
                if (rest.Count == 0) return "node needs an id: node <id>[,<id>...].";
                if (q.Seeded) return "node starts a question, so it cannot follow a |.";
                var byId = GraphQuery.Index(g);
                var named = rest
                    .SelectMany(w => w.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    .ToList();
                var found = named.Where(byId.ContainsKey).Select(id => byId[id]).ToList();
                if (found.Count == 0)
                    return $"no node '{named[0]}' - `search {named[0]}` finds what the graph does call it.";
                return Seed(q, found);
            }

            case "grep":
            {
                if (arg is null) return "grep needs a pattern: grep <regex> (quote it if it holds a |).";
                try { _ = new Regex(arg); }
                catch (ArgumentException ex) { return $"bad regex /{arg}/: {ex.Message}"; }

                // With nothing before it, the whole graph; after something, only what that found - which is
                // how a search gets narrowed to one feature without a scope flag to pick.
                var over = q.Seeded ? q.Hits.Select(h => h.Node) : g.Nodes;
                q.Hits = [.. GraphQuery.GrepNodes(over, arg, read)
                    .GroupBy(h => h.Node.Id, StringComparer.Ordinal)
                    .Select(byNode => new Hit(byNode.First().Node, [.. byNode.Select(h => (h.Line, h.Text))]))];
                q.Seeded = true;
                return null;
            }

            case "callers" or "callees":
            {
                if (!q.Seeded) return $"{name} needs somewhere to start: search, grep or node first.";
                var here = q.Hits.Select(h => h.Node.Id).ToHashSet(StringComparer.Ordinal);
                var byId = GraphQuery.Index(g);
                var seen = new HashSet<string>(StringComparer.Ordinal);
                var found = new List<GraphNode>();
                foreach (var e in g.Edges)
                {
                    // Containment is not a use. Left in, every member is "called" by the type declaring it and
                    // every type by its file, which buries the calls the question was about.
                    if (e.Relationship == EdgeRelationship.Contains) continue;
                    var (near, far) = name == "callers" ? (e.Target, e.Source) : (e.Source, e.Target);
                    if (!here.Contains(near) || !seen.Add(far)) continue;
                    if (byId.TryGetValue(far, out var n)) found.Add(n);
                }
                return Seed(q, found);
            }

            case "members":
            {
                if (!q.Seeded) return "members needs somewhere to start: search, grep or node first.";
                var here = q.Hits.Select(h => h.Node.Id).ToHashSet(StringComparer.Ordinal);
                return Seed(q, Contained(g, GraphQuery.Index(g), here));
            }

            case "like":
            {
                if (arg is null) return "like needs a pattern: like <regex>.";
                if (!q.Seeded) return "like narrows what is already found: search, grep or node first.";
                Regex rx;
                try { rx = new Regex(arg, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant); }
                catch (ArgumentException ex) { return $"bad regex /{arg}/: {ex.Message}"; }
                q.Hits = [.. q.Hits.Where(h => rx.IsMatch(h.Node.Id) || rx.IsMatch(h.Node.Label))];
                return null;
            }

            case "limit":
            {
                if (arg is null || !int.TryParse(arg, out var n) || n <= 0)
                    return $"limit needs a positive number (got '{arg ?? "nothing"}').";
                q.Hits = [.. q.Hits.Take(n)];
                return null;
            }

            case "ids" or "source" or "files" or "count":
            {
                if (!last) return $"{name} prints the answer, so nothing can follow it.";
                if (!q.Seeded) return $"{name} has nothing to print: search, grep or node first.";
                var room = 0;
                if (arg is not null && (!int.TryParse(arg, out room) || room <= 0))
                    return $"{name} takes how many to print, as a number (got '{arg}').";
                q.Sink = name;
                q.Room = room;
                return null;
            }

            default:
                return $"'{name}' is not a stage. If it is part of a regex, the | before it needs quoting.";
        }
    }

    private static string? Seed(Question q, IEnumerable<GraphNode> nodes)
    {
        q.Hits = [.. nodes.Select(n => new Hit(n, []))];
        q.Seeded = true;
        return null;
    }

    private static List<GraphNode> Contained(KnowledgeGraph g, Dictionary<string, GraphNode> byId,
                                             IReadOnlySet<string> parents) =>
        [.. g.Edges
            .Where(e => e.Relationship == EdgeRelationship.Contains && parents.Contains(e.Source))
            .Select(e => e.Target).Distinct(StringComparer.Ordinal)
            .Where(byId.ContainsKey).Select(id => byId[id])];

    // -- Printing it ------------------------------------------------------------

    private static string Print(KnowledgeGraph g, Question q, GraphQuery.ReadLines read)
    {
        if (!q.Seeded) return $"ask: no question.\n{Vocabulary}";
        return q.Sink switch
        {
            "count" => Tally(q, 0),
            "files" => Files(q),
            "source" => Source(g, q, read),
            _ => Ids(q),
        };
    }

    private static string Ids(Question q)
    {
        var room = q.Room > 0 ? q.Room : ShownIds;
        var sb = new StringBuilder();
        foreach (var h in q.Hits.Take(room))
        {
            sb.AppendLine($"  {h.Node.Id}{Where(h.Node)}");
            foreach (var (line, text) in h.Lines.Take(LinesPerNode)) sb.AppendLine($"      {line,5}: {Clip(text)}");
            if (h.Lines.Count > LinesPerNode)
                sb.AppendLine($"      ... +{h.Lines.Count - LinesPerNode} more matching line(s) here");
        }
        return sb.Append(Tally(q, Math.Min(room, q.Hits.Count))).ToString();
    }

    private static string Files(Question q)
    {
        var room = q.Room > 0 ? q.Room : ShownIds;
        var perFile = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var h in q.Hits.Where(h => h.Node.FilePath is { Length: > 0 }))
            perFile[h.Node.FilePath!] = perFile.GetValueOrDefault(h.Node.FilePath!) + Math.Max(1, h.Lines.Count);

        var sb = new StringBuilder();
        foreach (var (path, n) in perFile
                     .OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal).Take(room))
            sb.AppendLine($"  {n,5}  {path}");
        return sb.Append(Tally(q, 0)).ToString();
    }

    private static string Source(KnowledgeGraph g, Question q, GraphQuery.ReadLines read)
    {
        var room = q.Room > 0 ? q.Room : ShownSource;
        var spans = new SourceSpans();
        var sb = new StringBuilder();
        foreach (var h in q.Hits.Take(room))
        {
            if (h.Node.Type == NodeType.File) { sb.Append(Outline(g, h.Node)); continue; }
            if (GraphQuery.ReadSource(h.Node, read, SourceLines, spans) is not { } block)
            {
                sb.AppendLine($"  {h.Node.Id} - nothing to read (it is not a code node, or its file has moved).");
                continue;
            }
            sb.AppendLine($"// {block.RelativePath}:{block.StartLine}-{block.EndLine}   {h.Node.Id}");
            for (var i = 0; i < block.Lines.Count; i++) sb.AppendLine($"{block.StartLine + i,5}  {block.Lines[i]}");
            if (block.MoreLines > 0)
                sb.AppendLine($"      ... +{block.MoreLines} more line(s) - `graph code {h.Node.Id}` for all of it.");
        }
        return sb.Append(Tally(q, Math.Min(room, q.Hits.Count))).ToString();
    }

    /// <summary>
    /// A file has no block of its own, and printing the whole thing is the habit this verb exists to replace -
    /// so what it holds is the answer instead: every declaration in it, one line each, deepest name last so a
    /// member's id is its type's plus that tail.
    /// </summary>
    public static string Outline(KnowledgeGraph g, GraphNode file)
    {
        var byId = GraphQuery.Index(g);
        var types = Contained(g, byId, new HashSet<string>(StringComparer.Ordinal) { file.Id })
            .OrderBy(Line).ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"// {file.FilePath} - {types.Count} declaration(s). An id below + '/' + a member's tail "
                    + "names that member; `source` one of them rather than the file.");
        foreach (var t in types)
        {
            sb.AppendLine($"  {Line(t),5}  {t.Id}");
            foreach (var m in Contained(g, byId, new HashSet<string>(StringComparer.Ordinal) { t.Id }).OrderBy(Line))
                sb.AppendLine($"  {Line(m),5}      {Tail(m)}");
        }
        return sb.ToString();
    }

    private static string Tail(GraphNode n) =>
        n.Metadata?.GetValueOrDefault("ast") is { Length: > 0 } ast && ast.LastIndexOf('/') is var cut && cut > 0
            ? ast[(cut + 1)..]
            : n.Label;

    private static int Line(GraphNode n) =>
        n.Metadata?.GetValueOrDefault("line") is { } text && int.TryParse(text, out var line) ? line : 0;

    private static string Where(GraphNode n) =>
        n.Metadata?.GetValueOrDefault("line") is { Length: > 0 } line ? $"   line {line}" : "";

    private static string Clip(string text, int max = 160) =>
        text.Length <= max ? text : text[..(max - 1)] + "...";

    /// <summary>The one line every answer ends on: what was found, and how to see the part that was not
    /// printed - by widening the sink, never by asking again.</summary>
    private static string Tally(Question q, int shown)
    {
        var lines = q.Hits.Sum(h => h.Lines.Count);
        var files = q.Hits.Where(h => h.Node.FilePath is { Length: > 0 })
            .Select(h => h.Node.FilePath!).Distinct(StringComparer.OrdinalIgnoreCase).Count();

        var what = $"{q.Hits.Count} node(s)"
                 + (lines > 0 ? $", {lines} matching line(s)" : "")
                 + (files > 0 ? $", {files} file(s)" : "");
        var more = shown > 0 && shown < q.Hits.Count
            ? $" - showing {shown}; `{q.Sink} {q.Hits.Count}` prints the rest"
            : "";
        return what + more + ".";
    }
}
