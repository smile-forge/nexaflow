using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid.Git.Stages;

/// <summary>
/// Works the history out over the whole block: which branch each commit is made on — the one checked out above it, and the
/// main branch until anything else is — and what is not there to work with. A branch made twice, a branch checked out or
/// merged before it is made, a branch merged into itself, an id given to two commits, a commit picked that nothing above
/// has, and a commit kept as something a git graph does not keep, each say so where they are written.
/// </summary>
/// <param name="main">What the branch everything starts on is called, which the front matter may name.</param>
public sealed class ResolveGraph(string main) : IAstStage
{
    public string Name => "git:graph";

    public ContentNode Run(ContentNode tree)
    {
        var on = new Dictionary<ContentNode, string>();
        var wrong = new Dictionary<ContentNode, string>();
        var branches = new List<string> { main };
        var heads = new Dictionary<string, string>(StringComparer.Ordinal);
        var made = new Dictionary<string, (string Branch, IReadOnlyList<string> Parents)>(StringComparer.Ordinal);
        var ids = new List<string>();
        var current = main;
        var own = 0;

        foreach (var line in tree.SelfAndDescendants().Where(node => node.Kind == MermaidKinds.Line))
        {
            if (line.Stated() is not { } stated) continue;

            switch (stated.Kind)
            {
                case GitKinds.Branch:
                    if (Named(stated) is not { Length: > 0 } opened) break;

                    if (branches.Contains(opened, StringComparer.Ordinal)) wrong[stated] = $"A branch is made once, and {opened} is already one.";
                    else branches.Add(opened);

                    // A branch starts where the branch it is made from has got to.
                    if (heads.TryGetValue(current, out var branched)) heads[opened] = branched;
                    current = opened;
                    break;

                case GitKinds.Checkout:
                    if (Named(stated) is not { Length: > 0 } wanted) break;

                    if (!branches.Contains(wanted, StringComparer.Ordinal)) wrong[stated] = $"No branch {wanted} is made above this to check out.";
                    else current = wanted;
                    break;

                case GitKinds.Merge:
                    on[stated] = current;
                    Kept(stated, ids, wrong);

                    if (Named(stated) is { Length: > 0 } merged)
                    {
                        if (!branches.Contains(merged, StringComparer.Ordinal)) wrong[stated] = $"No branch {merged} is made above this to merge.";
                        else if (StringComparer.Ordinal.Equals(merged, current)) wrong[stated] = $"A branch is merged into another: check out the branch to merge {merged} into first.";
                    }

                    Commits(stated, current, heads, made, ref own);
                    break;

                case GitKinds.Commit:
                    on[stated] = current;
                    Kept(stated, ids, wrong);
                    Commits(stated, current, heads, made, ref own);
                    break;

                case GitKinds.Pick:
                    on[stated] = current;
                    Picked(stated, current, heads, made, wrong);
                    Commits(stated, current, heads, made, ref own);
                    break;
            }
        }

        if (on.Count == 0 && wrong.Count == 0) return tree;

        return AstRewrite.Each(tree, node =>
        {
            var said = on.TryGetValue(node, out var branch) ? node.Saying(GitKinds.Fact, GitRoles.On, branch) : node;
            return wrong.TryGetValue(node, out var reason) ? said.Saying(reason) : said;
        });
    }

    /// <summary>
    /// What a line commits: the id it takes — its own where it writes one — and what it follows, which is where its branch had
    /// got to, and the branch a merge brings in or the commit a cherry-pick takes.
    /// </summary>
    private static void Commits(ContentNode line, string branch, Dictionary<string, string> heads,
                                Dictionary<string, (string Branch, IReadOnlyList<string> Parents)> made, ref int own)
    {
        var picked = line.Kind == GitKinds.Pick;
        var written = Option(line, "id");
        var id = !picked && written is { Length: > 0 } ? written : "_" + own++;

        var parents = new List<string>();
        if (heads.TryGetValue(branch, out var parent)) parents.Add(parent);
        if (line.Kind == GitKinds.Merge && Named(line) is { Length: > 0 } merged && heads.TryGetValue(merged, out var second)) parents.Add(second);
        if (picked && written is { Length: > 0 }) parents.Add(written);

        made[id] = (branch, parents);
        heads[branch] = id;
    }

    /// <summary>
    /// What a cherry-pick has to have: a commit to take, written above it and on another branch; a branch with a commit on it
    /// already to go on; and, where what it takes is a merge, which of that merge's parents it takes.
    /// </summary>
    private static void Picked(ContentNode line, string branch, Dictionary<string, string> heads,
                               Dictionary<string, (string Branch, IReadOnlyList<string> Parents)> made, Dictionary<ContentNode, string> wrong)
    {
        if (Option(line, "id") is not { Length: > 0 } taken)
        {
            wrong[line] = "A cherry-pick names the commit it takes: cherry-pick id: \"Alpha\".";
            return;
        }

        if (!made.TryGetValue(taken, out var source))
        {
            wrong[line] = $"No commit above this has the id {taken}.";
            return;
        }

        if (!heads.ContainsKey(branch))
        {
            wrong[line] = $"A cherry-pick goes on a branch with a commit on it already, and nothing is committed on {branch} yet.";
            return;
        }

        if (StringComparer.Ordinal.Equals(source.Branch, branch))
        {
            wrong[line] = $"A cherry-pick takes a commit from another branch, and {taken} is on this one.";
            return;
        }

        if (source.Parents.Count <= 1) return;

        var from = Option(line, "parent");

        if (from is not { Length: > 0 })
            wrong[line] = $"A cherry-pick of a merge names which of its parents it takes: parent: \"{source.Parents[0]}\".";
        else if (!source.Parents.Contains(from, StringComparer.Ordinal))
            wrong[line] = $"'{from}' is no parent of {taken}.";
    }

    /// <summary>
    /// Whether a commit's id and type are ones Mermaid takes. Mermaid lets a commit take an id already given — a later
    /// reference to it meaning the newest — but refuses a merge one.
    /// </summary>
    private static void Kept(ContentNode line, List<string> ids, Dictionary<ContentNode, string> wrong)
    {
        if (Option(line, "id") is { Length: > 0 } id)
        {
            if (ids.Contains(id, StringComparer.Ordinal)) wrong[line] = $"A commit is given its id once, and {id} is already taken.";
            ids.Add(id);
        }

        if (Option(line, "type") is { Length: > 0 } kept && !GitGrammar.Kept.Contains(kept, StringComparer.OrdinalIgnoreCase))
            wrong[line] = $"A commit is {string.Join(", ", GitGrammar.Kept.SkipLast(1))} or {GitGrammar.Kept[^1]}, not '{kept}'.";
    }

    /// <summary>The branch a line names, or null where it names none yet.</summary>
    private static string? Named(ContentNode line) =>
        line.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Name)?.Inner(MermaidKinds.Words)?.Text;

    /// <summary>What a line's option says, without the quotes it may be written in — or null where the line does not set it.</summary>
    private static string? Option(ContentNode line, string key) =>
        line.Inner(MermaidKinds.Properties)?.Children
            .Where(property => property.Kind == MermaidKinds.Property)
            .Where(property => property.Children.Any(child => child.Kind == MermaidKinds.Key && child.Text.Equals(key, StringComparison.OrdinalIgnoreCase)))
            .Select(property => property.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Setting)?.Text)
            .Select(MermaidText.Bare)
            .FirstOrDefault();
}
