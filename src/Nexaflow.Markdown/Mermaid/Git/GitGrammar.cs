using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid.Git.Stages;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid.Git;

/// <summary>
/// What a <c>gitGraph</c> block says beyond the lines every diagram shares: which way it runs, a <c>title</c>, and the
/// commits, branches, checkouts, merges and cherry-picks making up its history.
///
/// <para>
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
public sealed class GitGrammar : IMermaidGrammar
{
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
    public static readonly IReadOnlyList<string> Committing = ["id", "tag", "type"];
    public static readonly IReadOnlyList<string> Picking = ["id", "parent", "tag"];
    public static readonly IReadOnlyList<string> Branching = ["order"];

    private const string Shape = "A git graph is commits, branches, checkouts, merges and cherry-picks: commit id: \"Alpha\".";

    /// <inheritdoc/>
    /// <remarks>Which way the graph runs, and the colon Mermaid closes its header with: <c>gitGraph LR:</c>.</remarks>
    public ContentNode? Header(string arguments)
    {
        var line = MermaidLine.Of(arguments);
        if (line.Done) return null;

        if (MermaidLine.Keyword(line.Written, [.. Ways]) is { } way)
        {
            line.Word(way, MermaidKinds.Setting, GitRoles.Way);
            line.Space();
        }

        line.Token(":");
        return line.Done ? line.Read(GitKinds.Direction) : null;
    }

    /// <inheritdoc/>
    public ContentNode? Statement(string text)
    {
        var line = MermaidLine.Of(text);

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

    /// <inheritdoc/>
    /// <remarks>Another commit, which is what a history is mostly made of.</remarks>
    public (string Text, int Caret)? Blank(ContentNode? above) => (CommitWord, CommitWord.Length);

    /// <inheritdoc/>
    /// <remarks>
    /// A quote typed into a value written in quotes goes in as the entity code standing for it, and a branch name is put in
    /// quotes to hold anything a branch is not named with.
    /// </remarks>
    public MermaidWriting? Escaping(ContentPart part, int caret, string text) =>
        MermaidWriting.Escape(part, caret, text, (_, said) => Bare(said))
        ?? (part.Kind == MermaidKinds.Setting ? MermaidWriting.InQuotes(caret, text) : null);

    /// <inheritdoc/>
    /// <remarks>A branch is named where it is made, and used wherever it is checked out or merged.</remarks>
    public IReadOnlyList<MermaidName> Names(ContentPart block)
    {
        var lines = block.SelfAndDescendants()
            .Where(part => part.Kind is GitKinds.Branch or GitKinds.Checkout or GitKinds.Merge)
            .Select(part => (part.Kind, Name: part.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Name)))
            .Where(line => line.Name is not null && line.Name.Words() is { Length: > 0 })
            .Select(line => (line.Kind, Part: line.Name!, Said: line.Name!.Words()!.Text))
            .ToList();

        return
        [
            .. lines
                .Where(line => line.Kind == GitKinds.Branch)
                .GroupBy(branch => branch.Said, StringComparer.Ordinal)
                .Select(branch => new MermaidName(branch.Key, branch.First().Part,
                    [
                        .. branch.Skip(1).Select(made => made.Part),
                        .. lines.Where(line => line.Kind != GitKinds.Branch && line.Said == branch.Key).Select(use => use.Part),
                    ])),
        ];
    }

    /// <inheritdoc/>
    /// <remarks>A branch's name goes in as it is, or in quotes to hold anything a branch is not named with.</remarks>
    public string Naming(string name) => Bare(name) ? name : "\"" + MermaidText.Quoted(name) + "\"";

    /// <inheritdoc/>
    /// <remarks>What the history means together — the branch each commit is on, and what is not there to check out, merge or pick (<see cref="ResolveGraph"/>).</remarks>
    public IEnumerable<IAstStage> Stages(MermaidBlock block, bool writing) => [new ResolveGraph(GitConfig.Read(block.Config).MainBranchName)];

    /// <inheritdoc/>
    /// <remarks>Where a branch's name is still to write.</remarks>
    public bool Holds(ContentNode? holder, ContentNode node) => node.Kind == MermaidKinds.Name;

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

        if (named)
        {
            if (!line.Name(GitRoles.Name, Letter)) return line.Shown(Shape);
            if (line.Done) return line.Read(kind);

            var after = line.At;
            line.Room();
            if (line.At == after) return line.Shown(Shape);
            if (line.Done) return line.Read(kind);
        }

        if (known is null || !line.Properties(known, what: What(word), spaced: true)) return line.Shown(Shape);

        return line.Done ? line.Read(kind) : line.Shown(Shape);
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
    private static bool Bare(string name) => name.Length > 0 && name.All(Letter);
}
