using Nexaflow.Core.Services;
using Nexaflow.Providers.Common;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// The conversation held in the AI bar: a request continues the runs before it that ended in a reply, and the conversation
/// lasts until it is ended — the reply dismissed or continued as a conversation — whatever ends before then.
/// </summary>
[TestClass]
[CoversNode("aibar-bar-reply")]
public class BarConversationTests
{
    private const string Style = "Lead with the answer.";

    private static void Now(Action keep) => keep();

    private static LlmMessage User(string text)      => new(LlmRole.User, text);
    private static LlmMessage Assistant(string text) => new(LlmRole.Assistant, text);

    [TestMethod]
    public void ARequest_ContinuesTheRunsBeforeIt_WithEverythingTheyAdded()
    {
        var bar   = new BarConversation();
        var first = bar.Run(Style, Now);
        first.Ran!([User("Which drive is fullest?"), Assistant("D:, at 91%.")]);

        var second = bar.Run(Style, Now);

        Assert.AreEqual(Style, second.ReplyStyle);
        CollectionAssert.AreEqual(new[] { "Which drive is fullest?", "D:, at 91%." }, second.History.Select(m => m.Text).ToArray());
    }

    [TestMethod]
    public void ARunThatEndedWithoutAReply_AddsNothing()
    {
        var bar = new BarConversation();
        bar.Run(Style, Now).Ran!([User("List the folder."), new LlmMessage(LlmRole.Tool, "interrupted")]);
        bar.Run(Style, Now).Ran!([User("Say it."), Assistant("  ")]);

        Assert.AreEqual(0, bar.Run(Style, Now).History.Count);
    }

    [TestMethod]
    public void Ended_TheNextRequestStartsAfresh_AndARunStillGoingThenAddsNothing()
    {
        var bar     = new BarConversation();
        bar.Run(Style, Now).Ran!([User("One."), Assistant("1.")]);
        bar.Answered("One.", "1.");
        bar.Touched([@"C:\notes.txt"]);
        var running = bar.Run(Style, Now);

        bar.End();
        running.Ran!([User("Two."), Assistant("2.")]);

        Assert.AreEqual(0, bar.Run(Style, Now).History.Count);
        Assert.AreEqual(0, bar.Exchanges.Count);
        Assert.AreEqual(0, bar.Paths.Count);
    }

    [TestMethod]
    public void ItsExchanges_AndTheFilesItTouched_AreKeptInOrder_EachFileOnce()
    {
        var bar = new BarConversation();
        bar.Answered("One.", "1.");
        bar.Touched([@"C:\a.txt"]);
        bar.Answered("Two.", "2.");
        bar.Touched([@"c:\A.TXT", @"C:\b.txt"]);

        CollectionAssert.AreEqual(new[] { ("One.", "1."), ("Two.", "2.") }, bar.Exchanges.ToArray());
        Assert.AreEqual(2, bar.Paths.Count);
    }

    [TestMethod]
    public void AConversationCondensedForARun_IsKeptCondensed_WithWhatTheRunAddedAfterIt()
    {
        var bar = new BarConversation();
        bar.Run(Style, Now).Ran!([User("one"), Assistant("first")]);
        bar.Run(Style, Now).Ran!([User("two"), Assistant("second")]);

        var run = bar.Run(Style, Now);
        run.HistoryCondensed!([User("Notes: one and two.\n\ntwo"), Assistant("second")]);
        run.Ran!([User("three"), Assistant("third")]);

        CollectionAssert.AreEqual(new[] { "Notes: one and two.\n\ntwo", "second", "three", "third" },
                                  bar.Run(Style, Now).History.Select(m => m.Text).ToArray());
    }
}
