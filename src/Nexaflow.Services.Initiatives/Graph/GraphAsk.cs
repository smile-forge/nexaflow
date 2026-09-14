using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
using Nexaflow.Services.Initiatives.Graph.Model;
using Nexaflow.Syntax;

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
/// An answer is the size of its question. What a turn costs a caller is everything already said plus what this
/// prints, so an answer that prints what was not asked for is paid for again on every turn after it. A grep
/// printed as source is the lines it matched with a few either side, not every declaration that holds one; a
/// listing gives each declaration's signature, so what a type offers is read without its bodies; ids are listed
/// under the file they share rather than each spelling it out; and an answer too long for one read is cut where
/// a block ends, with the question that prints the rest — <c>@3 more</c> — instead of being cut short by the tool
/// it was printed into, which replaces an overlong output with its first two kilobytes and leaves nothing to
/// continue from.
/// </para>
/// <para>
/// Pure: a loaded graph and a callback for reading source in, text out — so the CLI and the in-app assistant
/// can ask the same question and be told the same thing, exactly as with <see cref="GraphQuery"/>. The one
/// thing it cannot compute from those, what the compiler says, is asked of a <see cref="Diagnose"/> the caller
/// supplies when it holds a compiler.
/// </para>
/// </summary>
public static class GraphAsk
{
    /// <summary>What the answer said, and whether the question was one this vocabulary could read.</summary>
    public sealed record Answer(string Text, bool Ok);

    /// <summary>One thing the compiler or an analyzer reported, by repo-relative file and 1-based line.</summary>
    public sealed record Finding(string RelativePath, int Line, string Id, string Severity, string Message);

    /// <summary>
    /// What the compiler and each project's analyzers report for <paramref name="files"/>, or for the whole of each of
    /// <paramref name="projects"/> (by name or repo-relative <c>.csproj</c> path), keeping the ids <paramref name="ids"/>
    /// matches — every warning and error when it is null. <c>NotChecked</c> says what could not be asked, and why.
    /// </summary>
    public delegate (IReadOnlyList<Finding> Found, IReadOnlyList<string> NotChecked) Diagnose(
        IReadOnlyCollection<string> files, IReadOnlyCollection<string> projects, Regex? ids);

    /// <summary>Printed with every refusal, because the whole vocabulary is shorter than an explanation of
    /// which part of it was wrong.</summary>
    public const string Vocabulary = """
        start:  search <term> | grep <regex> [--from <id>] [--scope owned|hops] [--hops <n>] | node <id>[,<id>...]
                | diagnostics [<id-regex>] --project <name> | @ | @<n> | @<n> more
        narrow: callers | callees | members | owned | near <n> | grep <regex> | diagnostics [<id-regex>] | like <regex> | limit <n>
        print:  ids [n] | source [n] | blocks [n] | files | count      (ids, when the question says nothing)
        """;

    /// <summary>
    /// How much one answer prints before it is cut and continued with <c>@n more</c>. A caller's tool keeps about
    /// 30,000 characters of a command's output before swapping it for a two-kilobyte preview, so this stays well under
    /// that with room for a second answer in the same call.
    /// </summary>
    public const int PageChars = 20_000;

    private const int ShownIds = 40;
    private const int ShownSource = 8;
    private const int SourceLines = 240;
    private const int LinesPerNode = 3;

    /// <summary>Lines printed either side of a matched line — enough to see the statement it is in.</summary>
    private const int Context = 2;

    /// <summary>A node, and whatever lines of it a <c>grep</c> or <c>diagnostics</c> stage matched on the way past.</summary>
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
    /// <param name="diagnose">The compiler, for <c>diagnostics</c> — null where there is none to ask.</param>
    public static Answer Run(KnowledgeGraph graph, string script, GraphQuery.ReadLines read, AnswerHistory? history = null,
                             Diagnose? diagnose = null)
    {
        var questions = script.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .ToList();
        if (questions.Count == 0) return new Answer($"ask: no question.\n{Vocabulary}", false);

        // Shared out, so several questions in one call still fit in what one call can show.
        var budget = Math.Max(4_000, PageChars / questions.Count);

        var sb = new StringBuilder();
        var ok = true;
        foreach (var question in questions)
        {
            if (questions.Count > 1) sb.AppendLine($"-- {question}");
            var answer = One(graph, question, read, history, diagnose, budget);
            sb.AppendLine(answer.Text);
            ok &= answer.Ok;
        }
        return new Answer(sb.ToString().TrimEnd(), ok);
    }

    private static Answer One(KnowledgeGraph g, string question, GraphQuery.ReadLines read, AnswerHistory? history,
                              Diagnose? diagnose, int budget)
    {
        if (!Split(question, out var stages, out var bad)) return new Answer($"ask: {bad}\n{Vocabulary}", false);

        if (stages is [[{ Quoted: false } handle, { Quoted: false } word]]
            && handle.Text.StartsWith('@') && word.Text.Equals("more", StringComparison.OrdinalIgnoreCase))
            return More(handle.Text, history, budget);

        var q     = new Question();
        var asked = stages;
        var first = 0;

        // `@` or `@3` in place of the first stage continues an earlier answer. What is kept is the question with it spelled
        // out, so an answer continued from a continuation is still one question, asked again whole if it has to be.
        if (stages[0] is [{ Quoted: false } start] && start.Text.StartsWith('@'))
        {
            if (Continue(g, read, q, start.Text, history, diagnose, out var earlier) is { } refused)
                return new Answer($"ask: {refused}\n{Vocabulary}", false);
            asked = [.. earlier, .. stages.Skip(1)];
            first = 1;
        }

        for (var i = first; i < stages.Count; i++)
        {
            if (Stage(g, read, q, stages[i], last: i == stages.Count - 1, diagnose) is { } error)
                return new Answer($"ask: {error}\n{Vocabulary}", false);

            if (q.Seeded && q.Hits.Count == 0 && q.EmptiedBy is null && !IsPrint(stages[i]))
                q.EmptiedBy = string.Join(' ', stages[i].Select(w => w.Quoted ? $"\"{w.Text}\"" : w.Text));
        }

        if (!q.Seeded) return new Answer($"ask: no question.\n{Vocabulary}", true);

        // Numbered before it is printed, so what it prints can name the question that continues it.
        int? number = history?.Add(TextOf(asked.Where(s => !IsPrint(s))), [.. q.Hits.Select(h => (h.Node, h.Lines))]);

        var (body, tally) = Print(g, q, read, number);
        var notes = q.Notes.Count == 0 ? "" : string.Join('\n', q.Notes) + "\n";
        var text  = notes + Paged(body, tally, budget, history, number);
        return new Answer(number is { } n ? $"{text}   @{n}" : text, true);
    }

    /// <summary>
    /// Starts a question from an earlier answer: from what it found, when nothing that was found in has changed since, and
    /// otherwise by asking its question again. <paramref name="earlier"/> is that question's stages.
    /// </summary>
    private static string? Continue(KnowledgeGraph g, GraphQuery.ReadLines read, Question q, string handle, AnswerHistory? history,
                                    Diagnose? diagnose, out List<List<Word>> earlier)
    {
        earlier = [];
        if (Earlier(handle, history, out var entry) is { } refused) return refused;

        if (!Split(entry!.Question, out earlier, out var bad)) return $"@{entry.Number} could not be read back: {bad}";

        var byId = GraphQuery.Index(g);
        if (history!.IsCurrent(entry) && entry.Found.All(f => byId.ContainsKey(f.NodeId)))
        {
            q.Hits   = [.. entry.Found.Select(f => new Hit(byId[f.NodeId], f.Lines))];
            q.Seeded = true;
            if (q.Hits.Count == 0) q.EmptiedBy = handle;
            return null;
        }

        q.Notes.Add($"note: @{entry.Number} was asked again - something it was found in has changed since.");
        foreach (var stage in earlier)
            if (Stage(g, read, q, stage, last: false, diagnose) is { } error) return $"@{entry.Number} could not be asked again: {error}";
        return null;
    }

    /// <summary>The answer a handle names, or why there is none.</summary>
    private static string? Earlier(string handle, AnswerHistory? history, out AnswerHistory.Entry? entry)
    {
        entry = null;
        if (history is null)
            return "@ continues an earlier answer, and answers are kept by nfi's resident process - there are none here.";

        int? number = handle == "@" ? null : int.TryParse(handle.AsSpan(1), out var n) && n > 0 ? n : -1;
        if (number == -1) return $"'{handle}' is not an answer: @ is the last one, @<n> the one numbered n.";

        var (oldest, newest) = history.Range;
        entry = history.Get(number);
        if (entry is not null) return null;
        return newest == 0 ? "there is no earlier answer to continue yet."
                           : $"there is no answer {handle} - the ones kept are @{oldest} to @{newest}.";
    }

    /// <summary><c>@3 more</c>: the next part of an answer that was too long to print whole.</summary>
    private static Answer More(string handle, AnswerHistory? history, int budget)
    {
        if (Earlier(handle, history, out var entry) is { } refused) return new Answer($"ask: {refused}\n{Vocabulary}", false);

        if (history!.PageOf(entry!.Number) is not { } kept || kept.Shown >= kept.Lines.Count)
            return new Answer($"@{entry.Number} printed all of itself - there is no more of it.   @{entry.Number}", true);

        var (page, next) = Page(kept.Lines, kept.Shown, budget);
        history.Advance(entry.Number, next);

        var stale = history.IsCurrent(entry) ? ""
            : "note: something this answer was found in has changed since - this is the rest of it as it was printed; "
            + "ask it again for how it is now.\n";
        return new Answer($"{stale}{page}{Cut(kept.Lines.Count - next, entry.Number)}{kept.Tally}   @{entry.Number}", true);
    }

    /// <summary>Whether a stage prints rather than finds — which a continued answer leaves to the question continuing it.</summary>
    private static bool IsPrint(List<Word> stage) =>
        stage.Count > 0 && stage[0].Text.ToLowerInvariant() is "ids" or "source" or "blocks" or "files" or "count";

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

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];

            // A backslash keeps the character after it from being a quote or a stage break, and both stay in the word: `\"`
            // and `\|` already mean a literal quote and a literal bar to a regex, so the pattern means what was typed. Without
            // it a regex could not hold the quote it was quoted with, and a question with both kinds could not be asked at all.
            if (ch == '\\' && i + 1 < text.Length)
            {
                word.Append(ch).Append(text[++i]);
                started = true;
                continue;
            }

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

        if (open != '\0')
        {
            error = "a quote is left open. A quote inside a quoted pattern is written \\\" or \\', and a question that is awkward "
                  + "to quote can be asked on stdin: nfi ask --stdin.";
            return false;
        }
        if (found.Exists(s => s.Count == 0))
        {
            error = "an empty stage - two | with nothing between them, or a | at one end. "
                  + "A regex holding a | has to be quoted.";
            return false;
        }
        stages = found;
        return true;
    }

    private static string? Stage(KnowledgeGraph g, GraphQuery.ReadLines read, Question q, List<Word> words, bool last,
                                 Diagnose? diagnose)
    {
        var name = words[0].Text.ToLowerInvariant();
        var rest = words.Skip(1).ToList();

        if (name == "grep") return Grep(g, read, q, rest);
        if (name == "diagnostics") return Diagnostics(g, read, q, rest, diagnose);

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
                // A term is literal, so an escape written to get a quote or a bar past the question is taken back out of it.
                arg = Regex.Replace(arg, @"\\([""'|\\])", "$1");
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

                // A file's members are every declaration in it, not just its outermost types: `node file:X | members |
                // like Foo` is how one member of a file is found, and stopping at the types left that finding nothing.
                var files = q.Hits.Where(h => h.Node.Type == NodeType.File && h.Node.FilePath is { Length: > 0 })
                                  .Select(h => h.Node.FilePath!).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var inFiles = g.Nodes
                    .Where(n => n.Type is NodeType.Type or NodeType.Member && n.FilePath is { } p && files.Contains(p))
                    .OrderBy(n => n.FilePath, StringComparer.Ordinal).ThenBy(Line);

                var types = q.Hits.Where(h => h.Node.Type != NodeType.File).Select(h => h.Node.Id).ToHashSet(StringComparer.Ordinal);
                return Seed(q, inFiles.Concat(Contained(g, GraphQuery.Index(g), types)).DistinctBy(n => n.Id));
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

            case "ids" or "source" or "blocks" or "files" or "count":
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

    /// <summary>
    /// <c>diagnostics</c>: what the compiler and a project's own analyzers report, as the nodes they are reported in — each
    /// finding credited to the innermost declaration holding its line, with its id and message as the line it matched.
    /// <para>
    /// A warning is an answer somebody has already computed. "Which buttons have no automation id" is exactly what
    /// NXUI001 lists, and without this the way to it was to read the view whole and work it out again — three reads of a
    /// 500-line file in the run that prompted it, against one build filtered to the id. A finding in XML or XAML carries
    /// the element's <c>--at</c> path too, so the answer is also the address of the fix.
    /// </para>
    /// </summary>
    private static string? Diagnostics(KnowledgeGraph g, GraphQuery.ReadLines read, Question q, List<Word> rest, Diagnose? diagnose)
    {
        string? project = null;
        var pattern = new List<string>();
        for (var i = 0; i < rest.Count; i++)
        {
            var w = rest[i];
            if (!w.IsFlag) { pattern.Add(w.Text); continue; }
            if (w.Text != "--project")
                return $"diagnostics takes --project <name>; '{w.Text}' is not it - quote it if it is part of the id pattern.";
            if (i + 1 >= rest.Count) return "--project needs a value: a project's name, or its .csproj path.";
            project = rest[++i].Text;
        }

        if (diagnose is null)
            return "diagnostics asks the compiler, and there is none to ask here - it is answered by nfi on the command line.";

        Regex? ids = null;
        if (pattern.Count > 1)
            return $"diagnostics takes one id pattern (got '{string.Join(' ', pattern)}') - quote alternatives: \"NXUI001|CS0618\".";
        if (pattern.Count == 1)
        {
            try { ids = new Regex(pattern[0], RegexOptions.IgnoreCase | RegexOptions.CultureInvariant); }
            catch (ArgumentException ex) { return $"bad regex /{pattern[0]}/: {ex.Message}"; }
        }

        var files    = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var projects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (project is not null)
        {
            if (q.Seeded) return "diagnostics --project starts a question, so it cannot follow a | - the stage before it is already where it looks.";
            projects.Add(project);
        }
        else if (!q.Seeded)
        {
            return "diagnostics needs somewhere to look: --project <name>, or a stage in front of it - "
                 + "`node file:<path> | diagnostics`, `node product:<slug> | owned | diagnostics`.";
        }
        else
        {
            foreach (var path in q.Hits.Select(h => h.Node.FilePath).OfType<string>().Where(p => p.Length > 0))
                (path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ? projects : files).Add(path);

            if (q.Hits.Count > 0 && files.Count == 0 && projects.Count == 0)
            {
                var first = q.Hits[0].Node.Id;
                return $"diagnostics reads files, and nothing the stage before it found has one ({first} is not code). "
                     + $"`node {first} | owned | diagnostics` looks in the files it owns.";
            }
        }

        if (files.Count == 0 && projects.Count == 0) return Seed(q, []);

        var (found, notChecked) = diagnose(files, projects, ids);
        foreach (var why in notChecked) q.Notes.Add($"note: not checked - {why}");

        var inFile = g.Nodes.Where(n => n.FilePath is { Length: > 0 })
                            .GroupBy(n => n.FilePath!, StringComparer.OrdinalIgnoreCase)
                            .ToDictionary(x => x.Key, x => x.ToList(), StringComparer.OrdinalIgnoreCase);
        var signer = new Signer(read);
        var hits   = new Dictionary<string, Hit>(StringComparer.Ordinal);
        var order  = new List<Hit>();

        foreach (var d in found.OrderBy(d => d.RelativePath, StringComparer.Ordinal).ThenBy(d => d.Line))
        {
            if (Holding(inFile.GetValueOrDefault(d.RelativePath), d.Line) is not { } node)
            {
                q.Notes.Add($"note: {d.RelativePath}:{d.Line} {d.Id} {d.Message} - that file is not in the graph.");
                continue;
            }

            // The address before the message: a listing clips a long line, and the path is the part the next command needs.
            var at   = signer.XmlPathAt(d.RelativePath, d.Line) is { } path ? $" --at \"{path}\"" : "";
            var text = $"{d.Id} {d.Severity}{at}: {d.Message}";

            if (!hits.TryGetValue(node.Id, out var hit))
            {
                hits[node.Id] = hit = new Hit(node, []);
                order.Add(hit);
            }
            hit.Lines.Add((d.Line, text));
        }

        q.Hits   = order;
        q.Seeded = true;
        return null;
    }

    /// <summary>The innermost declaration of a file whose lines hold <paramref name="line"/>, else the file itself.</summary>
    private static GraphNode? Holding(List<GraphNode>? nodes, int line)
    {
        if (nodes is null) return null;
        return nodes.Where(n => n.Type != NodeType.File && Line(n) is var start and > 0 && start <= line && line <= Math.Max(start, EndLine(n)))
                    .OrderByDescending(Line).ThenBy(EndLine)
                    .FirstOrDefault()
            ?? nodes.FirstOrDefault(n => n.Type == NodeType.File);
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

    /// <summary>The answer's body, and the tally line it ends on — apart, so a body cut to fit still ends with its tally.</summary>
    private static (string Body, string Tally) Print(KnowledgeGraph g, Question q, GraphQuery.ReadLines read, int? number) =>
        q.Sink switch
        {
            "count"  => ("", Tally(q, 0)),
            "files"  => Files(q),
            "source" => Source(g, q, read, whole: false, number),
            "blocks" => Source(g, q, read, whole: true, number),
            _        => Ids(q, read, number),
        };

    /// <summary>
    /// The body as much of it fits in <paramref name="budget"/>, then the tally. The rest is kept with the answer, and the
    /// cut names the question that prints it; with nowhere to keep it, the cut says how to ask for less instead.
    /// </summary>
    private static string Paged(string body, string tally, int budget, AnswerHistory? history, int? number)
    {
        if (body.Length + tally.Length <= budget) return body + tally;

        var lines = body.TrimEnd('\n').Split('\n');
        var (page, next) = Page(lines, 0, budget - tally.Length);
        var left = lines.Length - next;

        if (history is null || number is not { } n)
            return $"{page}… {left} more line(s) not shown - ask for less: `like <regex>`, `limit <n>`, or fewer (`source 3`).\n{tally}";

        history.Keep(n, lines, next, tally);
        return page + Cut(left, n) + tally;
    }

    /// <summary>
    /// Lines from <paramref name="from"/> that fit in <paramref name="budget"/> characters, ending where a block ends when
    /// one does in the last part of the page — a declaration cut in half is two reads for one. Always at least one line.
    /// </summary>
    private static (string Page, int Next) Page(IReadOnlyList<string> lines, int from, int budget)
    {
        var end  = from;
        var used = 0;
        while (end < lines.Count && (end == from || used + lines[end].Length + 1 <= budget))
            used += lines[end++].Length + 1;

        if (end < lines.Count)
        {
            // A block starts at a header: `// id`, or a line that is not indented (a file a group of ids is under).
            var floor = from + (end - from) * 2 / 3;
            for (var i = end; i > floor; i--)
                if (lines[i].StartsWith("// ", StringComparison.Ordinal) || (lines[i].Length > 0 && lines[i][0] != ' ' && !char.IsDigit(lines[i][0])))
                {
                    end = i;
                    break;
                }
        }

        var sb = new StringBuilder();
        for (var i = from; i < end; i++) sb.Append(lines[i]).Append('\n');
        return (sb.ToString(), end);
    }

    private static string Cut(int left, int number) =>
        left > 0 ? $"… {left} more line(s) of this answer: ask '@{number} more'\n" : "";

    private static (string, string) Ids(Question q, GraphQuery.ReadLines read, int? number)
    {
        var room   = q.Room > 0 ? q.Room : ShownIds;
        var signer = new Signer(read);
        var sb     = new StringBuilder();

        // Under the file they share: the header is the id's front half, and each row the half that differs — which is all
        // most of an id is, repeated.
        foreach (var group in q.Hits.Take(room).GroupBy(h => h.Node.FilePath ?? "", StringComparer.OrdinalIgnoreCase))
        {
            var hits = group.ToList();
            if (group.Key.Length == 0 || hits.Count == 1)
            {
                foreach (var h in hits)
                {
                    sb.AppendLine(Row($"  {h.Node.Id}{Where(h.Node)}", h.Lines.Count == 0 ? signer.Of(h.Node) : null));
                    HitLines(sb, h, "      ", number);
                }
                continue;
            }

            var code = hits.Any(h => AstOf(h.Node).Length > 0);
            sb.AppendLine(code ? $"  code:{group.Key}#" : $"  file:{group.Key}");
            var width = Math.Min(40, hits.Max(h => Label(h.Node).Length));
            foreach (var h in hits.OrderBy(h => Line(h.Node)))
            {
                var line = Line(h.Node);
                sb.AppendLine(Row($"  {(line > 0 ? line.ToString() : ""),6}  {Label(h.Node).PadRight(width)}",
                                  h.Lines.Count == 0 ? signer.Of(h.Node) : null));
                HitLines(sb, h, "          ", number);
            }
        }
        return (sb.ToString(), Tally(q, Math.Min(room, q.Hits.Count)));

        static string Label(GraphNode n) => AstOf(n) is { Length: > 0 } ast ? ast : "(the file)";
    }

    private static void HitLines(StringBuilder sb, Hit h, string indent, int? number)
    {
        foreach (var (line, text) in h.Lines.Take(LinesPerNode)) sb.AppendLine($"{indent}{line,5}: {Clip(text)}");
        if (h.Lines.Count > LinesPerNode)
            sb.AppendLine($"{indent}  ... +{h.Lines.Count - LinesPerNode} more here - "
                        + (number is { } n ? $"ask '@{n} | source' for every one in context" : "`source` prints every one in context"));
    }

    private static (string, string) Files(Question q)
    {
        var room = q.Room > 0 ? q.Room : ShownIds;
        var perFile = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var h in q.Hits.Where(h => h.Node.FilePath is { Length: > 0 }))
            perFile[h.Node.FilePath!] = perFile.GetValueOrDefault(h.Node.FilePath!) + Math.Max(1, h.Lines.Count);

        var shown = perFile.OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal).Take(room).ToList();

        // Under the project they are in: a feature's files differ only past its directory, and saying the directory once
        // is most of the characters of a long list.
        var sb = new StringBuilder();
        foreach (var group in shown.GroupBy(p => ProjectDirectory(p.Key), StringComparer.OrdinalIgnoreCase)
                                   .OrderByDescending(x => x.Sum(p => p.Value)).ThenBy(x => x.Key, StringComparer.Ordinal))
        {
            var files = group.ToList();
            // Two files under a header is three lines for two, and saves nothing worth it.
            if (group.Key.Length == 0 || files.Count < 3)
            {
                foreach (var (path, n) in files) sb.AppendLine($"  {n,5}  {path}");
                continue;
            }
            sb.AppendLine($"         {group.Key}");
            foreach (var (path, n) in files) sb.AppendLine($"  {n,5}    {path[group.Key.Length..]}");
        }
        return (sb.ToString(), Tally(q, 0));
    }

    /// <summary>
    /// The directory a file is listed under: the deepest one named like a project (<c>Nexaflow.Features.Solver</c>), which
    /// is where its path stops saying something the others in it do not — else its top-level directory.
    /// </summary>
    private static string ProjectDirectory(string path)
    {
        var parts = path.Split('/');
        for (var i = parts.Length - 2; i > 0; i--)
            if (parts[i].Contains('.', StringComparison.Ordinal)) return string.Join('/', parts[..(i + 1)]) + "/";
        return parts.Length > 1 ? parts[0] + "/" : "";
    }

    /// <summary>
    /// Each node's source. A node a stage matched lines in prints those lines in context (<see cref="Windows"/>) unless
    /// <paramref name="whole"/> asks for its whole block; a file prints its outline; anything else its block.
    /// </summary>
    private static (string, string) Source(KnowledgeGraph g, Question q, GraphQuery.ReadLines read, bool whole, int? number)
    {
        var matched = !whole && q.Hits.Any(h => h.Lines.Count > 0);
        var room    = q.Room > 0 ? q.Room : matched ? ShownIds : ShownSource;
        var spans   = new SourceSpans();
        var sb      = new StringBuilder();
        foreach (var h in q.Hits.Take(room))
        {
            if (!whole && h.Lines.Count > 0) { Windows(sb, h, read, spans); continue; }
            if (h.Node.Type == NodeType.File) { sb.Append(Outline(g, h.Node, read)); continue; }
            if (GraphQuery.ReadSource(h.Node, read, SourceLines, spans) is not { } block)
            {
                sb.AppendLine($"  {h.Node.Id} - nothing to read (it is not a code node, or its file has moved).");
                continue;
            }
            sb.AppendLine($"// {h.Node.Id}");
            for (var i = 0; i < block.Lines.Count; i++) sb.AppendLine($"{block.StartLine + i,5}  {block.Lines[i]}");
            if (block.MoreLines > 0)
                sb.AppendLine($"      ... +{block.MoreLines} more line(s) - `graph code {h.Node.Id} --lines "
                            + $"{block.EndLine + 1}-{block.EndLine + block.MoreLines}` for the rest.");
        }
        return (sb.ToString(), Tally(q, Math.Min(room, q.Hits.Count)));
    }

    /// <summary>
    /// The lines a node matched, each with <see cref="Context"/> lines either side, kept inside the node's own block and
    /// merged where they meet. What a stage said about a line that is not the line itself — a diagnostic's message — is
    /// printed above it.
    /// </summary>
    private static void Windows(StringBuilder sb, Hit h, GraphQuery.ReadLines read, SourceSpans spans)
    {
        if (h.Node.FilePath is not { Length: > 0 } rel || read(rel) is not { } lines)
        {
            sb.AppendLine($"  {h.Node.Id} - its file cannot be read.");
            return;
        }

        int first = 1, last = lines.Length;
        if (h.Node.Type != NodeType.File && GraphQuery.ReadSource(h.Node, read, GraphQuery.BlockScanLines, spans) is { } block)
            (first, last) = (block.StartLine, Math.Min(lines.Length, block.EndLine + block.MoreLines));

        var ranges = new List<(int From, int To)>();
        foreach (var line in h.Lines.Select(l => l.Line).Where(l => l >= 1 && l <= lines.Length).Distinct().Order())
        {
            var (from, to) = (Math.Max(Math.Min(first, line), line - Context), Math.Min(Math.Max(last, line), line + Context));
            if (ranges.Count > 0 && from <= ranges[^1].To + 1) ranges[^1] = (ranges[^1].From, Math.Max(ranges[^1].To, to));
            else ranges.Add((from, to));
        }

        var said = h.Lines.Where(l => l.Line >= 1 && l.Line <= lines.Length
                                   && !string.Equals(l.Text.Trim(), lines[l.Line - 1].Trim(), StringComparison.Ordinal))
                          .ToLookup(l => l.Line, l => l.Text);

        sb.AppendLine($"// {h.Node.Id}");
        for (var r = 0; r < ranges.Count; r++)
        {
            if (r > 0) sb.AppendLine("      ⋮");
            for (var i = ranges[r].From; i <= ranges[r].To; i++)
            {
                foreach (var note in said[i]) sb.AppendLine($"    ! {note}");
                sb.AppendLine($"{i,5}  {lines[i - 1]}");
            }
        }
    }

    /// <summary>
    /// A file has no block of its own, and printing the whole thing is the habit this verb exists to replace -
    /// so what it holds is the answer instead: every declaration in it, one line each with its signature, deepest
    /// name last so a member's id is its type's plus that tail.
    /// </summary>
    public static string Outline(KnowledgeGraph g, GraphNode file, GraphQuery.ReadLines read)
    {
        var byId   = GraphQuery.Index(g);
        var signer = new Signer(read);
        var types  = Contained(g, byId, new HashSet<string>(StringComparer.Ordinal) { file.Id })
            .OrderBy(Line).ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"// {file.FilePath} - {types.Count} declaration(s). An id is code:{file.FilePath}#<path>, and a member's "
                    + "path is its type's + '/' + the tail shown; `graph code <id>` prints one.");
        foreach (var t in types)
        {
            var members = Contained(g, byId, new HashSet<string>(StringComparer.Ordinal) { t.Id }).OrderBy(Line).ToList();
            var width   = members.Count == 0 ? 0 : Math.Min(28, members.Max(m => Tail(m).Length));

            sb.AppendLine(Row($"  {Line(t),5}  {AstOf(t)}", signer.Of(t)));
            foreach (var m in members)
                sb.AppendLine(Row($"  {Line(m),5}      {Tail(m).PadRight(width)}", signer.Of(m)));
        }
        return sb.ToString();
    }

    private static string Row(string head, string? signature) =>
        signature is { Length: > 0 } ? $"{head}  {signature}" : head.TrimEnd();

    /// <summary>
    /// Signatures for the nodes of one answer, each file parsed once however many of its declarations are printed — and
    /// kept across answers by the file's content, since the same few files are listed again and again in a session.
    /// </summary>
    private sealed class Signer(GraphQuery.ReadLines read)
    {
        private static readonly ConcurrentDictionary<(string File, int Content), IReadOnlyList<DeclarationSignature>> Parsed = new();

        private readonly Dictionary<string, (string? Grammar, IReadOnlyList<DeclarationSignature> All)> _files =
            new(StringComparer.OrdinalIgnoreCase);

        public string? Of(GraphNode node)
        {
            if (node.Type is not (NodeType.Type or NodeType.Member) || node.FilePath is not { Length: > 0 } rel) return null;
            var (grammar, all) = In(rel);
            if (grammar is null) return null;

            // An XML element's name is its tag, which is not what the graph labels it by, so it is found by position alone.
            var name = TreeSitterLanguages.IsXml(grammar) ? null : node.Label;
            return DeclarationSignatures.Find(all, name, Line(node), EndLine(node))?.Text;
        }

        public string? XmlPathAt(string rel, int line)
        {
            var (grammar, all) = In(rel);
            return grammar is not null && TreeSitterLanguages.IsXml(grammar) ? DeclarationSignatures.Innermost(all, line)?.XmlPath : null;
        }

        private (string? Grammar, IReadOnlyList<DeclarationSignature> All) In(string rel)
        {
            if (_files.TryGetValue(rel, out var held)) return held;

            if (TreeSitterLanguages.ForEdit(rel) is not { } grammar || read(rel) is not { } lines)
                return _files[rel] = (null, []);

            var content = new HashCode();
            foreach (var line in lines) content.Add(line);
            var key = (rel, content.ToHashCode());

            if (!Parsed.TryGetValue(key, out var all))
            {
                if (Parsed.Count > 4_000) Parsed.Clear();
                Parsed[key] = all = DeclarationSignatures.Of(grammar, string.Join('\n', lines));
            }
            return _files[rel] = (grammar, all);
        }
    }

    private static string AstOf(GraphNode n) => n.Metadata?.GetValueOrDefault("ast") ?? "";

    private static string Tail(GraphNode n) =>
        AstOf(n) is { Length: > 0 } ast && ast.LastIndexOf('/') is var cut && cut > 0
            ? ast[(cut + 1)..]
            : AstOf(n) is { Length: > 0 } whole ? whole : n.Label;

    private static int Line(GraphNode n) =>
        n.Metadata?.GetValueOrDefault("line") is { } text && int.TryParse(text, out var line) ? line : 0;

    private static int EndLine(GraphNode n) =>
        n.Metadata?.GetValueOrDefault("endLine") is { } text && int.TryParse(text, out var line) ? line : 0;

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