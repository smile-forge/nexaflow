using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Pipeline;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;

namespace Nexaflow.Tests.Visuals.Markdown;

/// <summary>
/// A stage whose finding is slow to make is never waited on: the content is laid without it, what it finds is worked out away from
/// the thread that lays it, and the content is laid again with it once it lands — as a spelling checked in the background would be.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("background-reading")]
public class SlowStageTests
{
    private const string Named = "slowly-checked";

    /// <summary>A spelling checked slowly: every word it knows to be wrong, marked where it is written.</summary>
    private sealed class Misspelt : ISlowStage
    {
        public string Name => "test:misspelt";

        public object Find(ContentNode tree) =>
            tree.Leaves().Select(leaf => leaf.Text).Where(text => text.Contains("teh", StringComparison.Ordinal)).ToHashSet();

        public ContentNode Apply(ContentNode tree, object found) =>
            AstRewrite.Each(tree, node => node.IsLeaf && ((HashSet<string>)found).Contains(node.Text) ? node.Saying("Misspelt.") : node);
    }

    static SlowStageTests() =>
        ContentLanguages.Register(ContentLanguages.Markdown with
        {
            Reads = static word => word == Named,
            Stages = static (tree, show) => [.. ContentLanguages.Markdown.Stages(tree, show), new Misspelt()],
        });

    [TestMethod]
    public void WhatIsSlowToFindIsNotWaitedFor_AndIsHungOnTheTreeOnceItLands() => UiThread.Run(() =>
    {
        var engine = new ContentEngine();
        var source = $"Nothing checked teh words of {Guid.NewGuid():N} yet.";
        bool Marked() => engine.Reading!.Root.SelfAndDescendants().Any(part => part.Trouble == "Misspelt.");

        using var landed = new ManualResetEventSlim();
        engine.Reread += (_, _) => landed.Set();

        engine.Lay(Named, EditState.For(source), StyleFormat.Dark, 600, readOnly: true);
        Assert.IsFalse(Marked(), "laid at once, before anything was found");

        Assert.IsTrue(landed.Wait(TimeSpan.FromSeconds(10)), "and told when it was");

        engine.Forget();
        engine.Lay(Named, EditState.For(source), StyleFormat.Dark, 600, readOnly: true);
        Assert.IsTrue(Marked(), "then laid again with it");
    });

    [TestMethod]
    public void RunAsAnyOtherStage_ItHasFoundNothingYet()
    {
        var tree = ContentNode.Leaf(Kinds.Verbatim, "teh");

        Assert.AreSame(tree, ((IAstStage)new Misspelt()).Run(tree));
    }
}
