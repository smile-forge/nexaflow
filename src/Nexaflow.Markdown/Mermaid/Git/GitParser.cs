using System.Linq;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Binding;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Mermaid.Git.Stages;

namespace Nexaflow.Markdown.Mermaid.Git;

/// <summary>
/// Reads a git graph — a <c>gitGraph</c> block: which way it runs, a <c>title</c>, and the commits, branches, checkouts, merges and
/// cherry-picks making up its history — and says how what is written into one is spelled.
///
/// <para>
/// A diagram is a language of its own, written in Mermaid's syntax — a <c>mermaid</c> fence says only which
/// (<see cref="MermaidFenceParser"/>) — so this reads its own source rather than being handed rows of it. What a fence holds round
/// any diagram — the front matter, an <c>accTitle</c>, a <c>%%</c> comment, a directive, a binding, the space between lines — is
/// read with the shared pieces (<see cref="MermaidParser"/>). The header is optional, since a <c>gitGraph</c> fence already says what
/// the block is. A statement ends where its row does, as Mermaid reads one.
/// </para>/// <para>
/// The rules are Mermaid's. A commit is <c>commit</c> and the options written after it, one after another with only space
/// between — <c>commit id: "Alpha" type: HIGHLIGHT tag: "v1.0"</c>. A branch is made and checked out by <c>branch
/// develop</c>, which may say <c>order:</c> where its lane goes; <c>checkout</c> (or <c>switch</c>) moves to a branch;
/// <c>merge develop</c> commits the merge on the branch checked out; and <c>cherry-pick id: "Alpha"</c> takes a commit
/// from elsewhere. The way it runs follows the keyword, with a colon: <c>gitGraph LR:</c>.
/// </para>
/// <para>
/// What the history means together — which branch each commit is made on, whether a branch is there to check out, whether
/// a commit is there to pick — is a fact about the block rather than about one line, so it is the stage's
/// (<see cref="ResolveGraph"/>). A branch is named where it is made and wherever it is checked out or merged, so a rename
/// carries to all of them.
/// </para>
/// </summary>
public sealed class GitParser : ITranspile
{
    private GitParser()
    {
    }

    /// <summary>The language a git graph is written in: what a fence names it by, and the word its header is written with.</summary>
    public const string Language = "gitGraph";

    /// <summary>The words a git graph's header is written with.</summary>
    public static readonly string[] Keywords = [Language];

    /// <summary>What opens a comment running to the end of its row.</summary>
    private const string Comment = "%%";
    public const string CommitWord = "commit";
    public const string BranchWord = "branch";
    public const string CheckoutWord = "checkout";
    public const string SwitchWord = "switch";
    public const string MergeWord = "merge";
    public const string PickWord = "cherry-pick";

    /// <summary>The ways a git graph runs: commits across the page, down it, or up it.</summary>
    public static readonly IReadOnlyList<string> Ways = ["LR", "TB", "BT"];

    /// <summary>What a commit may be: an ordinary one, one undoing what came before, or one drawn to stand out.</summary>
    public static readonly IReadOnlyList<string> Kept = ["NORMAL", "REVERSE", "HIGHLIGHT"];

    /// <summary>What a commit, a merge, a cherry-pick and a branch may say about themselves.</summary>
    public static readonly IReadOnlyList<string> Committing = ["id", "tag", "type", "msg"];
    public static readonly IReadOnlyList<string> Picking = ["id", "parent", "tag"];
    public static readonly IReadOnlyList<string> Branching = ["order"];

    private const string Shape = "A git graph is commits, branches, checkouts, merges and cherry-picks: commit id: \"Alpha\".";

    /// <summary>Reads a block into its lines, every character kept.</summary>
    public static ContentNode Parse(string? source)
    {
        source ??= string.Empty;

        var lines = new List<ContentNode>();
        var at = 0;
        var headed = false;

        if (MermaidParser.Fences(source) is (var open, var close))
        {
            // Only blank lines come before front matter; that is what makes it front matter.
            for (; at < open.Start; at = Next(source, at, ref headed, lines)) { }

            lines.Add(MermaidParser.FrontMatter(source, open, close));
            at = close.Stop;
        }

        while (at < source.Length) at = Next(source, at, ref headed, lines);

        return new BlockNode(Language, lines, MermaidKinds.Block);
    }

    /// <inheritdoc/>
    /// <remarks>Spelled as Mermaid spells it: nothing a git graph writes is spelled a way of its own.</remarks>
    public static ContentChange? Rewrite(ContentChange change) => MermaidParser.Rewrite(change);

    /// <summary>The next line of the block, read and added to <paramref name="lines"/>, and where the one after it starts.</summary>
    /// <param name="headed">Whether the first line saying anything has been read: only it may be the header.</param>
    private static int Next(string source, int at, ref bool headed, List<ContentNode> lines)
    {
        var row = MermaidParser.Row.At(source, at);
        var (from, to) = row.Text(source);
        var text = source[from..to];

        if (text.Length == 0)
        {
            lines.Add(MermaidParser.Line(source, row.Start, from, [], to, row));
            return row.Stop;
        }

        if (text.StartsWith("%%{", StringComparison.Ordinal)) return MermaidParser.Directive(source, row, from, to, lines);

        if (text.StartsWith(Comment, StringComparison.Ordinal))
        {
            lines.Add(MermaidParser.Line(source, row.Start, from,
                                         [ContentNode.Leaf(Kinds.Comment, text, Roles.Trivia, offset: from)], to, row));
            return row.Stop;
        }

        // A binding standing on a line of its own after the header is lines supplied by whatever the diagram is shown against. Held
        // as written; read into its place before the diagram is worked over.
        if (BoundText.Path(text) is not null)
        {
            lines.Add(MermaidParser.Line(source, row.Start, from,
                [headed
                    ? ContentNode.Leaf(Kinds.BoundContent, text, offset: from)
                    : ContentNode.Shown(text, "A diagram is not bound whole: its first line names its type, and a binding after it supplies lines of it.", offset: from)],
                to, row));
            return row.Stop;
        }

        if (!headed)
        {
            headed = true;
            if (Heading(text, from) is { } header)
            {
                lines.Add(MermaidParser.Line(source, row.Start, from, [header], to, row));
                return row.Stop;
            }
        }

        if (MermaidParser.Accessibility(source, row, from, to, lines) is { } stop) return stop;

        // The statement, which ends where its row does, or where a string open at its end closes, as Mermaid reads one: as much of it
        // as is read is what was written, and the rest is the line's. A line that reads as nothing yet — a title still to be written —
        // is held whole.
        var end = Ended(source, from, row);
        if (Statement(source[from..end], from) is { } said) return MermaidParser.StatementLine(source, row.Start, from, said, end, lines);

        lines.Add(MermaidParser.Line(source, row.Start, from, [ContentNode.Leaf(MermaidKinds.Statement, text, offset: from)], to, row));
        return row.Stop;
    }

    /// <summary>
    /// Where the statement starting at <paramref name="from"/> ends: the end of its row, or of the row a string still open there
    /// closes on — Mermaid's strings run across line endings, a backslash keeping what follows it. A title is its row whatever it holds.
    /// </summary>
    private static int Ended(string source, int from, MermaidParser.Row row)
    {
        if (MermaidLine.Keyword(source[from..row.End], MermaidLine.TitleWord) is not null) return row.End;

        for (var at = from; at < row.End; at++)
        {
            if (source[at] == '%' && at + 1 < row.End && source[at + 1] == '%') return row.End;
            if (source[at] is not ('"' or '\'')) continue;

            var close = Closing(source, at);
            if (close < 0) return row.End;
            if (close >= row.End) return MermaidParser.Row.Containing(source, close).End;

            at = close;
        }

        return row.End;
    }

    /// <summary>The header — <c>gitGraph</c>, and which way it runs: <c>gitGraph LR:</c> — where the first line saying anything is one.</summary>
    private static ContentNode? Heading(string text, int at)
    {
        if (MermaidLine.Keyword(text, Keywords) is not { } keyword) return null;

        var pieces = new List<ContentNode> { ContentNode.Leaf(MermaidKinds.Keyword, text[..keyword.Length], Roles.Name, offset: at) };

        var rest = text[keyword.Length..];
        var gap = MermaidParser.Leading(rest);
        var after = at + keyword.Length + gap;

        if (gap > 0) pieces.Add(MermaidParser.Space(rest[..gap], at + keyword.Length));
        if (gap < rest.Length) pieces.Add(Way(rest[gap..], after) ?? ContentNode.Leaf(Kinds.Verbatim, rest[gap..], MermaidRoles.Arguments, offset: after));

        return ContentNode.Branch(MermaidKinds.Header, pieces, offset: at);
    }
    /// <summary>Which way the graph runs, and the colon Mermaid closes its header with: <c>gitGraph LR:</c> — null where it is neither.</summary>
    private static ContentNode? Way(string arguments, int at)
    {
        var line = MermaidLine.Of(arguments, at);
        if (line.Done) return null;

        if (MermaidLine.Keyword(line.Written, [.. Ways]) is { } way)
        {
            line.Word(way, MermaidKinds.Setting, GitRoles.Way);
            line.Space();
        }

        line.Token(":");
        return line.Done ? line.Read(GitKinds.Direction) : null;
    }

    /// <summary>A statement: a title, or a line of history.</summary>
    private static ContentNode? Statement(string text, int at)
    {
        var line = MermaidLine.Of(text, at, CommentAt);

        return MermaidLine.Keyword(line.Written, MermaidLine.TitleWord, CommitWord, BranchWord, CheckoutWord, SwitchWord, MergeWord, PickWord) switch
        {
            MermaidLine.TitleWord => line.Title(),
            CommitWord => Kind(line, CommitWord, GitKinds.Commit, Committing, named: false),
            MergeWord => Kind(line, MergeWord, GitKinds.Merge, Committing, named: true),
            PickWord => Kind(line, PickWord, GitKinds.Pick, Picking, named: false),
            BranchWord => Kind(line, BranchWord, GitKinds.Branch, Branching, named: true),
            CheckoutWord => Kind(line, CheckoutWord, GitKinds.Checkout, known: null, named: true),
            SwitchWord => Kind(line, SwitchWord, GitKinds.Checkout, known: null, named: true),
            _ => line.Shown(Shape),
        };
    }

    /// <summary>Whether a hole stands in <paramref name="node"/> while somebody writes in the block: where a branch's name is still to write.</summary>
    public static bool Holds(ContentNode? holder, ContentNode node) => node.Kind == MermaidKinds.Name;

    // ── Lines ───────────────────────────────────────────────────────────────

    /// <summary>
    /// A line of history: its word, the branch it names where it names one, and the options written after it, one after
    /// another — <paramref name="known"/> null for a line that takes none.
    /// </summary>
    private static ContentNode Kind(MermaidLine line, string word, string kind, IReadOnlyList<string>? known, bool named)
    {
        line.Word(word);
        if (line.Done) return line.Room().Read(kind);

        var spaced = line.At;
        line.Room();
        if (line.At == spaced) return line.Shown(Shape);

        // What follows the word is still to write.
        if (line.Done) return line.Read(kind);

        // A commit's message in quotes with no key before it, as older diagrams write one: commit "Initial commit".
        if (word == CommitWord && line.Next is '"' or '\'')
        {
            if (!Quoted(line, GitRoles.Message, MermaidKinds.Quoted)) return line.Shown(Shape);

            line.Room();
            if (line.Done) return line.Read(kind);
        }

        if (named)
        {
            // A branch's name in either quote, as Mermaid's strings are written, a backslash keeping what follows it: branch 'feature one'.
            if (!(line.Next is '"' or '\'' ? Quoted(line, GitRoles.Name, MermaidKinds.Name) : line.Name(GitRoles.Name, Letter))) return line.Shown(Shape);
            if (line.Done) return line.Read(kind);

            var after = line.At;
            line.Room();
            if (line.At == after) return line.Shown(Shape);
            if (line.Done) return line.Read(kind);
        }

        if (known is null || !line.Properties(known, what: What(word), spaced: true)) return line.Shown(Shape);

        return line.Done ? line.Read(kind) : line.Shown(Shape);
    }

    /// <summary>
    /// Words in quotes — either quote, as Mermaid's strings are written, a backslash keeping what follows it and the string running
    /// across a line ending to its closing quote — read as a piece of <paramref name="kind"/>, the quotes its own.
    /// </summary>
    private static bool Quoted(MermaidLine line, string role, string kind)
    {
        var written = line.Rest;
        var close = Closing(written, 0);
        if (close < 0) return line.Fail("What is written in quotes is closed with the quote it opened with.");

        var at = line.Begins;
        var quote = written[..1];

        line.Open();
        line.Add(ContentNode.Leaf(Kinds.Token, quote, Roles.Open, offset: at));
        line.Add(Said(written[1..close], at + 1, role));
        line.Add(ContentNode.Leaf(Kinds.Token, quote, Roles.Close, offset: at + close));
        line.Close(kind, role);
        return true;
    }

    /// <summary>
    /// What is between a string's quotes, as the words it is written with — and, where a backslash keeps a character or the string
    /// runs across a line ending, those as pieces of their own between them, each a <see cref="GitKinds.Escape"/> or a
    /// <see cref="GitKinds.Break"/>, so whatever says what the string says finds them without looking at the characters
    /// (<see cref="GitStandIns"/>).
    /// </summary>
    private static ContentNode Said(string text, int at, string role)
    {
        if (text.AsSpan().IndexOfAny('\\', '\n', '\r') < 0) return ContentWords.Of(text, Kinds.Words, role, at: at);

        var pieces = new List<ContentNode>();
        var from = 0;

        for (var place = 0; place < text.Length;)
        {
            var (kind, length) = text[place] switch
            {
                // A \n is a line break, as it is in every diagram's words, so it stays in them for whatever breaks their lines to find.
                '\\' when place + 1 < text.Length && text[place + 1] == 'n' => (null, 2),
                '\\' => (GitKinds.Escape, 2),
                '\r' when place + 1 < text.Length && text[place + 1] == '\n' => (GitKinds.Break, 2),
                '\r' or '\n' => (GitKinds.Break, 1),
                _ => ((string?)null, 0),
            };

            if (kind is null)
            {
                place += Math.Max(1, length);
                continue;
            }

            length = Math.Min(length, text.Length - place);
            if (place > from) pieces.Add(ContentNode.Leaf(Kinds.Words, text[from..place], role, offset: at + from));
            pieces.Add(ContentNode.Leaf(kind, text.Substring(place, length), offset: at + place));
            from = place += length;
        }

        if (from == 0) return ContentWords.Of(text, Kinds.Words, role, at: at);
        if (from < text.Length) pieces.Add(ContentNode.Leaf(Kinds.Words, text[from..], role, offset: at + from));

        return ContentNode.Branch(Kinds.Words, pieces, role, at);
    }

    /// <summary>Where the quote opening at <paramref name="open"/> closes, past every character a backslash keeps — or -1 where nothing closes it.</summary>
    private static int Closing(string text, int open)
    {
        for (var at = open + 1; at < text.Length; at++)
        {
            if (text[at] == '\\') at++;
            else if (text[at] == text[open]) return at;
        }

        return -1;
    }

    /// <summary>Where a comment closing a line starts, outside quotes of either kind — or -1.</summary>
    private static int CommentAt(string text)
    {
        for (var at = 0; at + 1 < text.Length; at++)
        {
            if (text[at] is '"' or '\'' && Closing(text, at) is var close and >= 0) at = close;
            else if (text[at] == '%' && text[at + 1] == '%') return at;
        }

        return -1;
    }

    /// <summary>
    /// What an option is set to, as Mermaid reads a string: its quotes — either kind — taken off, every character a backslash keeps
    /// kept, and its entity codes read.
    /// </summary>
    public static string Unquoted(string? value)
    {
        if (value is not { Length: >= 2 } || value[0] is not ('"' or '\'') || value[^1] != value[0]) return MermaidText.Bare(value);

        var inner = value[1..^1];
        var kept = new System.Text.StringBuilder(inner.Length);

        for (var at = 0; at < inner.Length; at++)
        {
            if (inner[at] == '\\' && at + 1 < inner.Length) at++;
            kept.Append(inner[at]);
        }

        return MermaidText.Decode(kept.ToString());
    }

    /// <summary>What a line's options belong to, for the reason where one of them is nothing that line sets.</summary>
    private static string What(string word) => word switch
    {
        CommitWord => "A commit",
        MergeWord => "A merge",
        PickWord => "A cherry-pick",
        _ => "A branch",
    };

    /// <summary>What a branch's name is made of — a path, as git names branches.</summary>
    private static bool Letter(char character) => char.IsLetterOrDigit(character) || character is '_' or '-' or '/' or '.';

    /// <summary>Whether a name can be written without quotes round it.</summary>
    internal static bool Bare(string name) => name.Length > 0 && name.All(Letter);
}
