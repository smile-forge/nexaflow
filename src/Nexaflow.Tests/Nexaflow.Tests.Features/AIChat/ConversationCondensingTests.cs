using NSubstitute;
using Nexaflow.Features.AIChat;
using Nexaflow.Features.AIChat.ViewModels;
using Nexaflow.Features.Common;
using Nexaflow.Tests.Fixtures;
using Page = Nexaflow.Features.Common.Page;

namespace Nexaflow.Tests.Features.AIChat;

/// <summary>
/// A conversation that outgrows its room in a session has its older messages condensed into notes, which stand in for them
/// from then on — the next request waiting for them rather than reading the conversation cut short. The notes are the
/// session's own; earlier sessions are the analysis made when they closed.
/// </summary>
[TestClass]
[CoversNode("aichat")]
public class ConversationCondensingTests
{
    private static IShellServices Shell()
    {
        var shell = Substitute.For<IShellServices>();
        shell.RunOnUiAsync(Arg.Any<Action>()).Returns(ci => { ci.Arg<Action>()(); return Task.CompletedTask; });
        return shell;
    }

    /// <summary>About 130 tokens of one word.</summary>
    private static string Said(string word) => string.Join(' ', Enumerable.Repeat(word, 120));

    [TestMethod]
    public async Task ASessionOutgrowingItsRoom_IsCondensed_TheNextRequestWaitingForIt_AndTheNotesStandInForIt()
    {
        var ai    = Substitute.For<IAIService>();
        var notes = new TaskCompletionSource<string?>();
        IReadOnlyList<(string Speaker, string Text)>? condensed = null;
        ai.CondenseConversationAsync(Arg.Do<IReadOnlyList<(string, string)>>(m => condensed = m), Arg.Any<string?>(), Arg.Any<CancellationToken>())
          .Returns(notes.Task);
        var vm = new ConversationViewModel(ai, Shell(), new AiChatConfig(), new Page());
        await vm.StartNew();
        vm.ContextWindow = 1000;

        vm.BeginResponse(Said("alpha"));
        vm.ShowFinal(Said("bravo"));
        Assert.IsTrue(vm.IsContextReady, "it still fits");

        vm.BeginResponse(Said("charlie"));
        vm.ShowFinal(Said("delta"));
        Assert.IsFalse(vm.IsContextReady, "the next request waits for the notes");
        CollectionAssert.AreEqual(new[] { "User", "Assistant" }, condensed!.Select(m => m.Speaker).ToArray(), "the older exchange");
        StringAssert.StartsWith(condensed[0].Text, "alpha");

        notes.SetResult("NOTES: alpha was asked, bravo answered.");
        await Task.Yield();
        Assert.IsTrue(vm.IsContextReady);

        var context = vm.GetContext();
        StringAssert.Contains(context, "[Earlier in this session, condensed]");
        StringAssert.Contains(context, "NOTES: alpha was asked, bravo answered.");
        Assert.IsFalse(context.Contains("alpha alpha"), "the condensed messages are not replayed");
        StringAssert.Contains(context, "charlie charlie", "the newest are, word for word");
    }
}
