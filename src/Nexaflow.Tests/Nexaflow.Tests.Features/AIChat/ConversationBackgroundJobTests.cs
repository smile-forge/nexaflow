using NSubstitute;
using Nexaflow.Features.AIChat;
using Nexaflow.Features.AIChat.ViewModels;
using Nexaflow.Features.Common;
using Nexaflow.Tests.Fixtures;
using Page = Nexaflow.Features.Common.Page;

namespace Nexaflow.Tests.Features.AIChat;

/// <summary>
/// Work a conversation's request left running in the background ends after the reply: what came of it joins the conversation
/// as the assistant's word and is saved with it — after the turn in progress, when one is, so a turn is never split by it.
/// </summary>
[TestClass]
[CoversNode("aibar-background-tasks")]
public class ConversationBackgroundJobTests
{
    private const string Made = @"Picture made. Saved to C:\Pictures\lighthouse.png and opened it.";

    /// <summary>A shell whose RunOnUiAsync runs the action where it is asked.</summary>
    private static IShellServices Shell()
    {
        var shell = Substitute.For<IShellServices>();
        shell.RunOnUiAsync(Arg.Any<Action>()).Returns(ci => { ci.Arg<Action>()(); return Task.CompletedTask; });
        return shell;
    }

    private static async Task<(ConversationViewModel Conversation, IAIService Ai)> NewConversationAsync()
    {
        var ai = Substitute.For<IAIService>();
        var vm = new ConversationViewModel(ai, Shell(), new AiChatConfig(), new Page());
        await vm.StartNew();
        return (vm, ai);
    }

    [TestMethod]
    public async Task WorkThatEndsAfterTheReply_JoinsTheConversation_AndIsSaved()
    {
        var (vm, ai) = await NewConversationAsync();

        vm.BackgroundJobEnded(Made);

        var said = vm.Messages.Last();
        Assert.AreEqual(Made, said.Text);
        Assert.IsFalse(said.IsUser);
        CollectionAssert.Contains(vm.Conversation!.Messages, said);
        await ai.Received().SaveConversationAsync(vm.Conversation);
    }

    [TestMethod]
    public async Task WorkThatEndsDuringATurn_JoinsTheConversationAfterIt()
    {
        var (vm, _) = await NewConversationAsync();
        vm.BeginResponse("Now draw a boat.");

        vm.BackgroundJobEnded(Made);
        Assert.IsFalse(vm.Messages.Any(m => m.Text == Made), "the turn in progress is not split by it");

        vm.ShowFinal("Drawing the boat.");
        CollectionAssert.AreEqual(new[] { "Now draw a boat.", "Drawing the boat.", Made },
                                  vm.Conversation!.Messages.Select(m => m.Text).ToArray());
    }
}
