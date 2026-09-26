using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid.Git.Stages;

/// <summary>
/// Works the history out over the whole block, once: which branch each commit is made on — the one checked out above it, and
/// the main branch until anything else is — what each commit follows, where it stands along the history, and the lane each
/// branch takes; and what is not there to work with. A branch made twice, a branch checked out or merged before it is made, a
/// branch merged into itself, an id given to two commits, a commit picked that nothing above has, and a commit kept as
/// something a git graph does not keep, each say so where they are written.
///
/// <para>
/// A commit follows the last commit on the branch it is made on, a merge follows the branch it brings in as well, and a
/// cherry-pick follows the commit it takes — the newest given that id, as Mermaid reads an id given twice. One after another,
/// every commit takes the next place along; with <c>parallelCommits</c>, one past the furthest of what it follows, so branches
/// made together run level. A branch takes its lane from its <c>order:</c> as Mermaid orders them — a branch asking nothing
/// ordered as "0." and how many were made before it, so before any asking for 1 or more — and the branch everything starts on
/// is the front matter's <c>mainBranchName</c>, at its <c>mainBranchOrder</c>.
/// </para>
/// </summary>
/// <param name="config">What the front matter asks for: what the main branch is called and where it goes, and whether commits run side by side.</param>
public sealed class ResolveGraph(GitConfig config) : IAstStage
{
    public string Name => "git:graph";

    /// <summary>A commit worked out: its line, the branch it is on, what it is called, what it follows and takes, and where it stands.</summary>
    private sealed record Made(ContentNode Line, string Branch, string Id, List<int> Follows, int Takes, int Position);

    public ContentNode Run(ContentNode tree)
    {
        var main = config.MainBranchName;
        var wrong = new Dictionary<ContentNode, string>();
        var branches = new List<(ContentNode? Line, string Name, double? Order)> { (null, main, config.MainBranchOrder) };
        var heads = new Dictionary<string, int>(StringComparer.Ordinal);
        var ids = new Dictionary<string, int>(StringComparer.Ordinal);
        var given = new HashSet<string>(StringComparer.Ordinal);
        var commits = new List<Made>();
        var current = main;
        var own = 0;

        foreach (var line in tree.SelfAndDescendants().Where(node => node.Kind == MermaidKinds.Line))
        {
            if (line.Stated() is not { } stated) continue;

            switch (stated.Kind)
            {
                case GitKinds.Branch:
                    if (Named(stated) is not { Length: > 0 } opened) break;

                    if (Known(opened)) wrong[stated] = $"A branch is made once, and {opened} is already one.";
                    else branches.Add((stated, opened, MermaidNumber.Read(Option(stated, "order"))));

                    // A branch starts where the branch it is made from has got to.
                    if (heads.TryGetValue(current, out var branched)) heads[opened] = branched;
                    current = opened;
                    break;

                case GitKinds.Checkout:
                    if (Named(stated) is not { Length: > 0 } wanted) break;

                    if (!Known(wanted)) wrong[stated] = $"No branch {wanted} is made above this to check out.";
                    else current = wanted;
                    break;

                case GitKinds.Merge:
                    Kept(stated);

                    if (Named(stated) is { Length: > 0 } merged)
                    {
                        if (!Known(merged)) wrong[stated] = $"No branch {merged} is made above this to merge.";
                        else if (StringComparer.Ordinal.Equals(merged, current)) wrong[stated] = $"A branch is merged into another: check out the branch to merge {merged} into first.";
                    }

                    Commit(stated);
                    break;

                case GitKinds.Commit:
                    Kept(stated);
                    Commit(stated);
                    break;

                case GitKinds.Pick:
                    Picked(stated);
                    Commit(stated);
                    break;
            }
        }

        // A lane each, in the order the branches ask for; the branch everything starts on asks for nought where the front matter does not say.
        var lanes = branches
            .Select((branch, made) => (branch.Name, Order: made == 0 ? branch.Order ?? 0 : branch.Order ?? Unasked(made)))
            .OrderBy(branch => branch.Order)
            .Select((branch, lane) => (branch.Name, lane))
            .ToDictionary(branch => branch.Name, branch => branch.lane, StringComparer.Ordinal);

        var makes = branches.Where(branch => branch.Line is not null).ToDictionary(branch => branch.Line!, branch => lanes[branch.Name]);
        var placed = commits.Select((commit, at) => (commit.Line, at)).ToDictionary(commit => commit.Line, commit => commit.at);

        tree = AstRewrite.Each(tree, node =>
        {
            ContentNode said = placed.TryGetValue(node, out var at)
                ? new GitCommitNode(node, lanes[commits[at].Branch], commits[at].Position, commits[at].Follows, commits[at].Takes)
                : makes.TryGetValue(node, out var lane) ? new GitBranchNode(node, lane) : node;

            return wrong.TryGetValue(node, out var reason) ? said.Saying(reason) : said;
        });

        return new GitBlockNode(tree, config, lanes[main]);

        bool Known(string branch) => branches.Exists(made => StringComparer.Ordinal.Equals(made.Name, branch));

        // What a line commits: the id it takes — its own where it writes one — what it follows, and where it stands.
        void Commit(ContentNode line)
        {
            var picked = line.Kind == GitKinds.Pick;
            var written = Option(line, "id");
            var id = !picked && written is { Length: > 0 } ? written : "_" + own++;

            var follows = new List<int>();
            if (heads.TryGetValue(current, out var head)) follows.Add(head);
            if (line.Kind == GitKinds.Merge && Named(line) is { Length: > 0 } merged && heads.TryGetValue(merged, out var second)) follows.Add(second);

            var takes = picked && written is { Length: > 0 } && ids.TryGetValue(written, out var taken) ? taken : -1;
            if (takes >= 0) follows.Add(takes);

            var position = config.ParallelCommits ? follows.Select(before => commits[before].Position + 1).DefaultIfEmpty(0).Max() : commits.Count;

            ids[id] = heads[current] = commits.Count;
            commits.Add(new Made(line, current, id, follows, takes, position));
        }

        // What a cherry-pick has to have: a commit to take, written above it and on another branch; a branch with a commit on it
        // already to go on; and, where what it takes is a merge, which of that merge's parents it takes.
        void Picked(ContentNode line)
        {
            if (Option(line, "id") is not { Length: > 0 } taken)
            {
                wrong[line] = "A cherry-pick names the commit it takes: cherry-pick id: \"Alpha\".";
                return;
            }

            if (!ids.TryGetValue(taken, out var source))
            {
                wrong[line] = $"No commit above this has the id {taken}.";
                return;
            }

            if (!heads.ContainsKey(current))
            {
                wrong[line] = $"A cherry-pick goes on a branch with a commit on it already, and nothing is committed on {current} yet.";
                return;
            }

            if (StringComparer.Ordinal.Equals(commits[source].Branch, current))
            {
                wrong[line] = $"A cherry-pick takes a commit from another branch, and {taken} is on this one.";
                return;
            }

            var parents = commits[source].Follows;
            if (parents.Count <= 1) return;

            var from = Option(line, "parent");

            if (from is not { Length: > 0 })
                wrong[line] = $"A cherry-pick of a merge names which of its parents it takes: parent: \"{commits[parents[0]].Id}\".";
            else if (!parents.Exists(parent => StringComparer.Ordinal.Equals(commits[parent].Id, from)))
                wrong[line] = $"'{from}' is no parent of {taken}.";
        }

        // Whether a commit's id and type are ones Mermaid takes. Mermaid lets a commit take an id already given — a later
        // reference to it meaning the newest — but refuses a merge one.
        void Kept(ContentNode line)
        {
            if (Option(line, "id") is { Length: > 0 } id && !given.Add(id)) wrong[line] = $"A commit is given its id once, and {id} is already taken.";

            if (Option(line, "type") is { Length: > 0 } kept && !GitGrammar.Kept.Contains(kept, StringComparer.OrdinalIgnoreCase))
                wrong[line] = $"A commit is {string.Join(", ", GitGrammar.Kept.SkipLast(1))} or {GitGrammar.Kept[^1]}, not '{kept}'.";
        }
    }

    /// <summary>Where Mermaid orders a branch that asks for nowhere: the number written as "0." and how many were made before it.</summary>
    private static double Unasked(int made)
    {
        var scale = 10.0;
        while (scale <= made) scale *= 10;
        return made / scale;
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
