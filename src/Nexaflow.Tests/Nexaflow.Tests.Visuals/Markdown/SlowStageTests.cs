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
using Nexaflow.Markdown.Layout;
using Nexaflow.Markdown.Layout.Prose;
using Nexaflow.Visuals.Text.Painting;
using System.Collections.Concurrent;

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

    private const string InTurn = "checked-in-turn";

    /// <summary>What every <see cref="TakingTurns"/> takes turns with.</summary>
    private static readonly object Turns = new();

    /// <summary>Held shut until the test lets the first piece of work finish, with what each piece found, in order, and how many ran at once.</summary>
    private static readonly ManualResetEventSlim TurnTaken = new(), TurnBegun = new();
    private static readonly ConcurrentQueue<string> FoundInTurn = new();
    private static int _doingInTurn, _mostAtOnce;

    /// <summary>A slow stage taking turns with every other: each finding is the text it was found in, the first held until it is let go.</summary>
    private sealed class TakingTurns : ISlowStage
    {
        public string Name => "test:in-turn";

        public object? TakesTurnsWith => Turns;

        public object Find(ContentNode tree)
        {
            var doing = Interlocked.Increment(ref _doingInTurn);
            for (var most = _mostAtOnce; doing > most; most = _mostAtOnce) Interlocked.CompareExchange(ref _mostAtOnce, doing, most);

            TurnBegun.Set();
            TurnTaken.Wait(TimeSpan.FromSeconds(10));

            var text = string.Concat(tree.Leaves().Select(leaf => leaf.Text));
            FoundInTurn.Enqueue(text);
            Interlocked.Decrement(ref _doingInTurn);

            return text;
        }

        public ContentNode Apply(ContentNode tree, object found) => tree;
    }

    static SlowStageTests()
    {
        ContentLanguages.Register(ContentLanguages.Markdown with
        {
            Reads = static word => word == Named,
            Stages = static (tree, show) => [.. ContentLanguages.Markdown.Stages(tree, show), new Misspelt()],
        });

        ContentLanguages.Register(ContentLanguages.Markdown with
        {
            Reads = static word => word == InTurn,
            Stages = static (tree, show) => [.. ContentLanguages.Markdown.Stages(tree, show), new TakingTurns()],
        });
    }

    [TestMethod]
    public void WhatIsSlowToFindIsNotWaitedFor_AndIsHungOnTheTreeOnceItLands() => UiThread.Run(() =>
    {
        var engine = new ContentEngine();
        var source = $"Nothing checked teh words of {Guid.NewGuid():N} yet.";
        bool Marked() => engine.Readings.SelectMany(unit => unit.SelfAndDescendants()).Any(part => part.Trouble == "Misspelt.");

        using var landed = new ManualResetEventSlim();
        engine.Reread += (_, _) => landed.Set();

        engine.Lay(Named, EditState.For(source), WpfStyles.Dark, 600, readOnly: true);
        Assert.IsFalse(Marked(), "laid at once, before anything was found");

        Assert.IsTrue(landed.Wait(TimeSpan.FromSeconds(10)), "and told when it was");

        engine.Forget();
        engine.Lay(Named, EditState.For(source), WpfStyles.Dark, 600, readOnly: true);
        Assert.IsTrue(Marked(), "then laid again with it");
    });

    [TestMethod]
    public void WorkTakingTurns_IsDoneAPieceAtATime_TheNewestAskedFirst() => UiThread.Run(() =>
    {
        var asked = Enumerable.Range(0, 4).Select(at => $"Version {at} of {Guid.NewGuid():N}").ToList();
        void Ask(string text) => new ContentEngine().Lay(InTurn, EditState.For(text), WpfStyles.Dark, 600, readOnly: true);

        // The first is being found when the rest are asked for, as a reading typed in asks again with every keystroke.
        Ask(asked[0]);
        Assert.IsTrue(TurnBegun.Wait(TimeSpan.FromSeconds(10)), "the first was begun");

        foreach (var text in asked.Skip(1)) Ask(text);
        TurnTaken.Set();

        Assert.IsTrue(SpinWait.SpinUntil(() => FoundInTurn.Count == asked.Count, TimeSpan.FromSeconds(10)), "every piece was done");
        Assert.AreEqual(1, _mostAtOnce, "one piece at a time");
        CollectionAssert.AreEqual(new[] { asked[0], asked[3], asked[2], asked[1] }, FoundInTurn.ToArray(), "the newest asked first");
    });

    [TestMethod]
    public void RunAsAnyOtherStage_ItHasFoundNothingYet()
    {
        var tree = ContentNode.Leaf(Kinds.Verbatim, "teh");

        Assert.AreSame(tree, ((IAstStage)new Misspelt()).Run(tree));
    }
}
