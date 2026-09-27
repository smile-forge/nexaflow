using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Nexaflow.Markdown.Binding;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Binding;

/// <summary>
/// A graph bound into content a piece at a time: it keeps which nodes are opened, walks what they show, and lets only the
/// last walk asked for land.
/// </summary>
[TestClass]
[CoversNode("mermaid")]
public class BoundGraphTests
{
    /// <summary>A graph whose walk says which nodes were opened, landing at once.</summary>
    private static BoundGraph<string> Listing() =>
        new((opened, _) => Task.FromResult(string.Join(",", opened.Order(StringComparer.Ordinal))), graph => $"[{graph}]");

    [TestMethod]
    public void AWalkThatLandsIsWhatItSays()
    {
        var graph = Listing();
        var changed = 0;
        graph.Changed += (_, _) => changed++;

        graph.Walk();

        Assert.AreEqual("[]", graph.Text);
        Assert.AreEqual(1, changed);
        Assert.IsFalse(graph.Walking);
    }

    [TestMethod]
    public void OpeningANodeWalksWithItOpened()
    {
        var graph = Listing();

        graph.Expand("b", open: true);
        graph.Expand("a", open: true);

        Assert.AreEqual("[a,b]", graph.Text);
        CollectionAssert.AreEquivalent(new[] { "a", "b" }, graph.Opened.ToArray());
    }

    [TestMethod]
    public void OpeningWhatIsOpenAlreadyWalksNothing()
    {
        var graph = Listing();
        graph.Expand("a", open: true);

        var changed = 0;
        graph.Changed += (_, _) => changed++;

        graph.Expand("a", open: true);
        graph.Expand("z", open: false);

        Assert.AreEqual(0, changed);
    }

    [TestMethod]
    public void ResettingClosesEverything()
    {
        var graph = Listing();
        graph.Expand("a", open: true);

        graph.Reset();

        Assert.AreEqual("[]", graph.Text);
        Assert.AreEqual(0, graph.Opened.Count);
    }

    [TestMethod]
    public void KeysAreToldApartAsTheHostSays()
    {
        var graph = new BoundGraph<string>((opened, _) => Task.FromResult(string.Join(",", opened)), graph => graph,
                                           StringComparer.OrdinalIgnoreCase);

        graph.Expand("KERNEL32.dll", open: true);
        graph.Expand("kernel32.DLL", open: true);

        Assert.AreEqual(1, graph.Opened.Count);
    }

    [TestMethod]
    public void AWalkThatWasOvertakenNeverLands()
    {
        var slow = new TaskCompletionSource<string>();
        var walks = new Queue<Task<string>>([slow.Task, Task.FromResult("second")]);

        var graph = new BoundGraph<string>((_, _) => walks.Dequeue(), graph => graph);
        var landed = new List<string>();
        graph.Walked += (_, walked) => landed.Add(walked);

        graph.Walk();
        graph.Walk();
        slow.SetResult("first");

        Assert.AreEqual("second", graph.Text, "what the reader asked for last is what shows");
        CollectionAssert.AreEqual(new[] { "second" }, landed);
    }

    [TestMethod]
    public void AnOvertakenWalkIsToldToGiveUp()
    {
        var cancelled = false;
        var first = true;

        var graph = new BoundGraph<string>((_, walking) =>
        {
            if (!first) return Task.FromResult("done");

            first = false;
            walking.Register(() => cancelled = true);
            return new TaskCompletionSource<string>().Task;
        }, graph => graph);

        graph.Walk();
        graph.Walk();

        Assert.IsTrue(cancelled);
    }

    [TestMethod]
    public void AWalkThatFallsOverSaysWhy()
    {
        var graph = new BoundGraph<string>((_, _) => Task.FromException<string>(new InvalidOperationException("no such file")),
                                           graph => graph);
        var changed = 0;
        graph.Changed += (_, _) => changed++;

        graph.Walk();

        Assert.AreEqual("no such file", graph.Fault?.Message);
        Assert.AreEqual(1, changed, "what is shown is told, so it can stop waiting");
        Assert.IsFalse(graph.Walking);
    }
}
