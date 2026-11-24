using System.Runtime.ExceptionServices;
using NSubstitute;
using Nexaflow.Features.AIChat;
using Nexaflow.Features.AIChat.ViewModels;
using Nexaflow.Features.Common;
using Page = Nexaflow.Features.Common.Page;   // disambiguate from System.Windows.Controls.Page
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.AIChat;

/// <summary>
/// The conversation page names every turn asked on it by the conversation it holds, and the models that kept that conversation
/// between turns are told to let it go once the page no longer holds it: the page closed, or another conversation loaded or
/// started in its place.
/// </summary>
[TestClass]
[CoversNode("aichat")]
public class ConversationForgettingTests
{
    private static readonly ConversationRecord First  = new() { Id = "c1" };
    private static readonly ConversationRecord Second = new() { Id = "c2" };

    private static IAIService Service()
    {
        var ai = Substitute.For<IAIService>();
        ai.LoadConversationsAsync().Returns(Task.FromResult<IEnumerable<ConversationRecord>>([First, Second]));
        ai.RestoreContextPages(Arg.Any<ConversationRecord>()).Returns(_ => (IReadOnlyList<Page>)[]);
        return ai;
    }

    /// <summary>Runs <paramref name="body"/> on an STA thread, as the page's view-model is made and used.</summary>
    private static void Sta(Func<Task> body)
    {
        Exception? error = null;
        var t = new Thread(() =>
        {
            try { body().GetAwaiter().GetResult(); }
            catch (Exception ex) { error = ex; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error is not null) ExceptionDispatchInfo.Throw(error);
    }

    [TestMethod]
    public void TheTurnsAskedOnThePage_AreTurnsOfTheConversationItHolds() => Sta(async () =>
    {
        var convo = new ConversationViewModel(Service(), Substitute.For<IShellServices>(), new AiChatConfig(), new Page());

        await convo.LoadAsync("c1");

        Assert.AreEqual("c1", convo.ConversationName);
    });

    [TestMethod]
    public void ClosingThePage_ForgetsItsConversation() => Sta(async () =>
    {
        var ai    = Service();
        var owner = new Page();
        var convo = new ConversationViewModel(ai, Substitute.For<IShellServices>(), new AiChatConfig(), owner);
        await convo.LoadAsync("c1");

        owner.RaiseClosed();

        await ai.Received(1).ForgetConversationAsync("c1");
    });

    [TestMethod]
    public void AnotherConversationLoadedOrStarted_ForgetsTheOneItReplaced_AndReloadingTheSameForgetsNothing() => Sta(async () =>
    {
        var ai    = Service();
        var convo = new ConversationViewModel(ai, Substitute.For<IShellServices>(), new AiChatConfig(), new Page());
        await convo.LoadAsync("c1");
        await convo.LoadAsync("c1");   // the tab reactivated: the same conversation, still held
        await ai.DidNotReceiveWithAnyArgs().ForgetConversationAsync(default!);

        await convo.LoadAsync("c2");
        await ai.Received(1).ForgetConversationAsync("c1");

        await convo.StartNew();
        await ai.Received(1).ForgetConversationAsync("c2");
    });
}
