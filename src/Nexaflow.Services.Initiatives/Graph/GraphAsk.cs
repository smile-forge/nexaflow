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
/// The one concession is <c>grep</c>'s own flags. <c>graph grep --from X --scope owned</c> is what a caller
/// already knows, and typing it inside a question used to search for the literal text <c>--scope owned</c>
/// and report nothing found. So <c>grep</c> takes them and means by them exactly what the verb does — they
/// are spelled-out <c>node</c>, <c>owned</c> and <c>near</c> stages — and every other flag in a stage is
/// refused with the stage that does its job, rather than read as part of a pattern.
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
        start:  search <term> | grep <regex> [--from <id>] [--scope owned|hops] [--hops <n>] | node <id>[,<id>...] | @ | @<n>
        narrow: callers | callees | members | owned | near <n> | grep <regex> | like <regex> | limit <n>
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

        /// <summary>Said above the answer — how it was arrived at, when that changes what it means.</summary>
        public List<string> Notes = [];

        /// <summary>The stage that left nothing, so a zero names where the question went empty.</summary>
        public string? EmptiedBy;
    }

    /// <summary>One word of a stage, and whether it was quoted — a quoted word is never a flag.</summary>
    private readonly record struct Word(string Text, bool Quoted)
    {
        public bool IsFlag => !Quoted && Text.StartsWith("--", StringComparison.Ordinal) && Text.Length > 2;
    }

    /// <summary>
    /// Answers every question in <paramref name="script"/> — one per line, so a pattern may contain a
    /// semicolon and a <c>#</c> line is a comment. Ok is false if any of them could not be read, which is
    /// what the CLI turns into an exit code.
    /// </summary>
    /// <param name="history">The answers given so far, which <c>@</c> refers back to — null where nothing keeps them.</param>
    public static Answer Run(KnowledgeGraph graph, string script, GraphQuery.ReadLines read, AnswerHistory? history = null)
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
            var answer = One(graph, question, read, history);
            sb.AppendLine(answer.Text);
            ok &= answer.Ok;
        }
        return new Answer(sb.ToString().TrimEnd(), ok);
    }

    private static Answer One(KnowledgeGraph g, string question, GraphQuery.ReadLines read, AnswerHistory? history)
    {
        if (!Split(question, out var stages, out var bad)) return new Answer($"ask: {bad}\n{Vocabulary}", false);

        var q     = new Question();
        var asked = stages;
        var first = 0;

        // `@` or `@3` in place of the first stage continues an earlier answer. What is kept is the question with it spelled
        // out, so an answer continued from a continuation is still one question, asked again whole if it has to be.
        if (stages[0] is [{ Quoted: false } handle] && handle.Text.StartsWith('@'))
        {
            if (Continue(g, read, q, handle.Text, history, out var earlier) is { } refused)
                return new Answer($"ask: {refused}\n{Vocabulary}", false);
            asked = [.. earlier, .. stages.Skip(1)];
            first = 1;
        }

        for (var i = first; i < stages.Count; i++)
        {
            if (Stage(g, read, q, stages[i], last: i == stages.Count - 1) is { } error)
                return new Answer($"ask: {error}\n{Vocabulary}", false);

            if (q.Seeded && q.Hits.Count == 0 && q.EmptiedBy is null)
                q.EmptiedBy = string.Join(' ', stages[i].Select(w => w.Quoted ? $"\"{w.Text}\"" : w.Text));
        }

        var printed = Print(g, q, read);
        if (history is null || !q.Seeded) return new Answer(printed, true);

        var number = history.Add(TextOf(asked.Where(s => !IsPrint(s))), [.. q.Hits.Select(h => (h.Node, h.Lines))]);
        return new Answer($"{printed}   @{number}", true);
    }

    /// <summary>
    /// Starts a question from an earlier answer: from what it found, when nothing that was found in has changed since, and
    /// otherwise by asking its question again. <paramref name="earlier"/> is that question's stages.
    /// </summary>
    private static string? Continue(KnowledgeGraph g, GraphQuery.ReadLines read, Question q, string handle, AnswerHistory? history,
                                    out List<List<Word>> earlier)
    {
        earlier = [];
        if (history is null)
            return "@ continues an earlier answer, and answers are kept by nfi's resident process - there are none here.";

        int? number = handle == "@" ? null : int.TryParse(handle.AsSpan(1), out var n) && n > 0 ? n : -1;
        if (number == -1) return $"'{handle}' is not an answer: @ is the last one, @<n> the one numbered n.";

        var (oldest, newest) = history.Range;
        if (history.Get(number) is not { } entry)
            return newest == 0 ? "there is no earlier answer to continue yet."
                               : $"there is no answer {handle} - the ones kept are @{oldest} to @{newest}.";

        if (!Split(entry.Question, out earlier, out var bad)) return $"@{entry.Number} could not be read back: {bad}";

        var byId = GraphQuery.Index(g);
        if (history.IsCurrent(entry) && entry.Found.All(f => byId.ContainsKey(f.NodeId)))
        {
            q.Hits   = [.. entry.Found.Select(f => new Hit(byId[f.NodeId], f.Lines))];
            q.Seeded = true;
            return null;
        }

        q.Notes.Add($"note: @{entry.Number} was asked again - something it was found in has changed since.");
        foreach (var stage in earlier)
            if (Stage(g, read, q, stage, last: false) is { } error) return $"@{entry.Number} could not be asked again: {error}";
        return null;
    }

    /// <summary>Whether a stage prints rather than finds — which a continued answer leaves to the question continuing it.</summary>
    private static bool IsPrint(List<Word> stage) => stage.Count > 0 && stage[0].Text.ToLowerInvariant() is "ids" or "source" or "files" or "count";

    /// <summary>Stages as the question text they came from, quoting the words that were quoted.</summary>
    private static string TextOf(IEnumerable<List<Word>> stages) =>
        string.Join(" | ", stages.Select(stage => string.Join(' ', stage.Select(w =>
            !w.Quoted ? w.Text : w.Text.Contains('\'', StringComparison.Ordinal) ? $"\"{w.Text}\"" : $"'{w.Text}'"))));

    // -- Reading the question ---------------------------------------------------

    /// <summary>
    /// Stages, then words. Quotes are honoured before <c>|</c> is looked for, because a regex is the most
    /// likely thing to carry one: <c>grep "Foo|Bar"</c> is one stage, and an unquoted alternation would
    /// otherwise be silently read as two.
    /// </summary>
    private static bool Split(string text, out List<List<Word>> stages, out string? error)
    {
        stages = [];
        error = null;

        // `stages` is an out parameter, and a local function cannot capture one - so the stages are gathered
        // here and handed over at the end.
        var found = new List<List<Word>>();
        var words = new List<Word>();
        var word = new StringBuilder();
        var open = '\0';
        var started = false;
        var quoted = false;

        void EndWord()
        {
            if (!started) return;
            words.Add(new Word(word.ToString(), quoted));
            word.Clear();
            started = false;
            quoted = false;
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

            if (ch is '\'' or '"') { open = ch; started = true; quoted = true; continue; }
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

    private static string? Stage(KnowledgeGraph g, GraphQuery.ReadLines read, Question q, List<Word> words, bool last)
    {
        var name = words[0].Text.ToLowerInvariant();
        var rest = words.Skip(1).ToList();

        if (name == "grep") return Grep(g, read, q, rest);

        // Every other stage takes no flags. Read as part of a term, one silently searched for itself.
        if (rest.FindIndex(w => w.IsFlag) is var flagAt and >= 0 && rest[flagAt].Text is var flag)
            return $"{name} takes no flags, and '{flag}' is not part of a {name} term - quote it if it is. "
                 + "In a question, what a flag would do is a stage: `node <id> | owned | grep <regex>` for "
                 + "--scope owned, `node <id> | near <n>` for --hops.";

        var arg = rest.Count > 0 ? string.Join(' ', rest.Select(w => w.Text)) : null;

        switch (name)
        {
            case "search":
            {
                if (arg is null) return "search needs a term: search <term>.";
                if (q.Seeded) return "search starts a question, so it cannot follow a |.";

                // By name first; when nothing is called that, the source is searched instead — and the answer
                // says so, because "named X" and "mentions X" are different claims.
                var found = GraphQuery.Find(g, arg, read);
                if (!found.BySource) return Seed(q, found.Nodes);

                var lines = found.Lines.GroupBy(h => h.Node.Id, StringComparer.Ordinal)
                                       .ToDictionary(x => x.Key, x => x.Select(h => (h.Line, h.Text)).ToList());
                q.Hits = [.. found.Nodes.Select(n => new Hit(n, lines[n.Id]))];
                q.Seeded = true;
                q.Notes.Add($"note: nothing is named '{arg}', so the source was searched - these contain it.");
                return null;
            }

            case "node":
            {
                if (rest.Count == 0) return "node needs an id: node <id>[,<id>...].";
                if (q.Seeded) return "node starts a question, so it cannot follow a |.";
                return Nodes(g, q, rest.Select(w => w.Text));
            }

            case "callers" or "callees":
            {
                if (!q.Seeded) return $"{name} needs somewhere to start: search, grep or node first.";
                if (arg is not null) return $"{name} takes nothing after it (got '{arg}').";
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
                if (arg is not null) return $"members takes nothing after it (got '{arg}').";
                var here = q.Hits.Select(h => h.Node.Id).ToHashSet(StringComparer.Ordinal);
                return Seed(q, Contained(g, GraphQuery.Index(g), here));
            }

            case "owned":
            {
                if (!q.Seeded) return "owned needs something to be owned by: node, search or grep first.";
                if (arg is not null) return $"owned takes nothing after it (got '{arg}').";
                return Owned(g, q, [.. q.Hits.Select(h => h.Node)]);
            }

            case "near":
            {
                if (!q.Seeded) return "near needs somewhere to start: node, search or grep first.";
                if (arg is null || !int.TryParse(arg, out var hops) || hops < 0)
                    return $"near needs how many hops, as a number (got '{arg ?? "nothing"}').";
                return Seed(q, Near(g, q.Hits.Select(h => h.Node), hops));
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

    /// <summary>
    /// <c>grep</c>, with the flags <c>graph grep</c> takes meaning what they mean there. <c>--from</c> is a
    /// <c>node</c> stage in front of it, <c>--scope owned</c> an <c>owned</c> stage, and <c>--hops</c> (or
    /// <c>--scope hops</c>, or <c>--from</c> on its own) a <c>near</c> stage — so the flag and the stage cannot
    /// give two answers to one question. Without a flag, it searches what the question has found so far, or the
    /// whole graph when it opens the question.
    /// </summary>
    private static string? Grep(KnowledgeGraph g, GraphQuery.ReadLines read, Question q, List<Word> rest)
    {
        string? from = null, scope = null, hopsText = null;
        var pattern = new List<string>();
        for (var i = 0; i < rest.Count; i++)
        {
            var w = rest[i];
            if (!w.IsFlag) { pattern.Add(w.Text); continue; }

            if (w.Text is not ("--from" or "--scope" or "--hops"))
                return $"grep takes --from <id>, --scope owned|hops and --hops <n>; '{w.Text}' is none of them - "
                     + "quote it if it is part of the pattern.";
            if (i + 1 >= rest.Count) return $"{w.Text} needs a value.";

            var value = rest[++i].Text;
            switch (w.Text)
            {
                case "--from":  from = value; break;
                case "--scope": scope = value; break;
                default:        hopsText = value; break;
            }
        }

        if (pattern.Count == 0) return "grep needs a pattern: grep <regex> (quote it if it holds a |).";
        var regex = string.Join(' ', pattern);
        try { _ = new Regex(regex); }
        catch (ArgumentException ex) { return $"bad regex /{regex}/: {ex.Message}"; }

        // The same checks `graph grep` makes, with the same words, so the two cannot disagree about a scope.
        if (scope is not (null or "hops" or "owned")) return $"--scope must be hops or owned (got '{scope}').";
        if (scope == "owned" && hopsText is not null)
            return "--scope owned ignores radius - drop --hops, or drop --scope.";
        var hops = 2;
        if (hopsText is not null && (!int.TryParse(hopsText, out hops) || hops < 0))
            return $"--hops needs a number (got '{hopsText}').";

        if (from is not null)
        {
            if (q.Seeded)
                return "grep --from starts a question, so it cannot follow a | - the stage before it is already "
                     + "where it searches.";
            if (Nodes(g, q, [from]) is { } missing) return missing;
        }

        var scoped = scope is not null || hopsText is not null || from is not null;
        if (scoped && !q.Seeded)
            return $"{(scope == "owned" ? "--scope owned" : "--hops")} is relative to a node: give --from <id>, "
                 + "or put a stage in front of the grep.";

        if (scope == "owned")
        {
            if (Owned(g, q, [.. q.Hits.Select(h => h.Node)]) is { } error) return error;
        }
        else if (scoped)
        {
            Seed(q, Near(g, q.Hits.Select(h => h.Node), hops));
        }
        else if (q.Seeded && q.Hits.Count > 0 && q.Hits.All(h => h.Node.FilePath is not { Length: > 0 }))
        {
            // Grepping a feature node searched a node with no source and found nothing - which reads as "this
            // feature never mentions it". What the caller meant is what the feature owns.
            var first = q.Hits[0].Node.Id;
            return $"grep reads source, and nothing the stage before it found has any ({first} is not code). "
                 + $"`node {first} | owned | grep {regex}` searches the files it owns.";
        }

        var over = q.Seeded ? q.Hits.Select(h => h.Node) : g.Nodes;
        q.Hits = [.. GraphQuery.GrepNodes(over, regex, read)
            .GroupBy(h => h.Node.Id, StringComparer.Ordinal)
            .Select(byNode => new Hit(byNode.First().Node, [.. byNode.Select(h => (h.Line, h.Text))]))];
        q.Seeded = true;
        return null;
    }

    /// <summary>Seeds the question with the named nodes, refusing — and saying which — when any of them is not
    /// one the graph has, rather than quietly carrying on with the rest.</summary>
    private static string? Nodes(KnowledgeGraph g, Question q, IEnumerable<string> words)
    {
        var byId = GraphQuery.Index(g);
        var named = words
            .SelectMany(w => w.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToList();
        if (named.Count == 0) return "node needs an id: node <id>[,<id>...].";

        var missing = named.Where(id => !byId.ContainsKey(id)).ToList();
        if (missing.Count > 0)
            return $"no node '{missing[0]}'"
                 + (missing.Count > 1 ? $" (nor {string.Join(", ", missing.Skip(1).Select(m => $"'{m}'"))})" : "")
                 + $" - `search {missing[0]}` finds what the graph does call it.";

        return Seed(q, named.Distinct(StringComparer.Ordinal).Select(id => byId[id]));
    }

    /// <summary>Every node in a file that what the question has found owns — see
    /// <see cref="GraphQuery.OwnedFiles"/>, which is what <c>graph grep --scope owned</c> uses too.</summary>
    private static string? Owned(KnowledgeGraph g, Question q, IReadOnlyList<GraphNode> from)
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in from) files.UnionWith(GraphQuery.OwnedFiles(g, node.Id));

        if (files.Count == 0)
            return $"{(from.Count == 1 ? $"'{from[0].Id}' owns" : "none of those own")} no files - there are no "
                 + "snaplinks to code to follow.";

        return Seed(q, g.Nodes.Where(n => n.FilePath is { Length: > 0 } p && files.Contains(p)));
    }

    /// <summary>Everything within <paramref name="hops"/> edges of any of <paramref name="from"/>.</summary>
    private static List<GraphNode> Near(KnowledgeGraph g, IEnumerable<GraphNode> from, int hops)
    {
        var adjacency = GraphQuery.Adjacency(g);
        var byId = GraphQuery.Index(g);
        var reached = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in from)
            reached.UnionWith(GraphQuery.Bfs(adjacency, node.Id, hops).Keys);
        return [.. reached.Where(byId.ContainsKey).Select(id => byId[id])];
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
        var body = q.Sink switch
        {
            "count" => Tally(q, 0),
            "files" => Files(q),
            "source" => Source(g, q, read),
            _ => Ids(q),
        };
        return q.Notes.Count == 0 ? body : string.Join('\n', q.Notes) + "\n" + body;
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
    /// printed - by widening the sink, never by asking again. A zero says which stage left nothing.</summary>
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
        var empty = q.Hits.Count == 0 && q.EmptiedBy is { } stage ? $" - `{stage}` left nothing" : "";
        return what + more + empty + ".";
    }
}
