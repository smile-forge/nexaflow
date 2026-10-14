using System.Runtime.ExceptionServices;
using System.Windows.Threading;
using Nexaflow.Core.ViewModels;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// When the reply card above the AI bar shows: only with something to show; while the pointer is over the reply or the card,
/// with a moment's grace to cross between them; pinned by a click until closed; and unasked for a plan waiting on its answer.
/// </summary>
[TestClass]
[CoversNode("aibar-response-overlay")]
public class ReplyCardTests
{
    private static readonly TimeSpan Grace = TimeSpan.FromMilliseconds(50);

    [TestMethod]
    public void WithNothingToShow_ItNeverShows()
    {
        var card = new ReplyCard(Grace) { IsPinned = true, IsRequired = true };
        card.PointerOver(true);

        Assert.IsFalse(card.IsOpen);
    }

    [TestMethod]
    public void Hovered_ItShows_AndStaysAMomentAfterThePointerLeaves() => OnUi(async () =>
    {
        var card = new ReplyCard(Grace) { HasContent = true };

        card.PointerOver(true);
        Assert.IsTrue(card.IsOpen);

        card.PointerOver(false);
        Assert.IsTrue(card.IsOpen, "the pointer may be crossing to the card");
        card.PointerOver(true);
        await Task.Delay(Grace * 4);
        Assert.IsTrue(card.IsOpen, "it came back in time");

        card.PointerOver(false);
        await Task.Delay(Grace * 4);
        Assert.IsFalse(card.IsOpen);
    });

    [TestMethod]
    public void Pinned_ItStaysUntilClosed() => OnUi(async () =>
    {
        var card = new ReplyCard(Grace) { HasContent = true };
        card.PointerOver(true);
        card.TogglePinCommand.Execute(null);
        card.PointerOver(false);
        await Task.Delay(Grace * 4);

        Assert.IsTrue(card.IsOpen);
        card.CloseCommand.Execute(null);
        Assert.IsFalse(card.IsOpen);
    });

    [TestMethod]
    public void APlanWaitingOnItsAnswer_IsShownUnasked()
    {
        var card = new ReplyCard(Grace) { HasContent = true, IsRequired = true };

        Assert.IsTrue(card.IsOpen);
    }

    /// <summary>Runs <paramref name="body"/> on a thread with a dispatcher of its own, which the card's grace runs on.</summary>
    private static void OnUi(Func<Task> body)
    {
        Exception? failed = null;
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            var frame = new DispatcherFrame();
            dispatcher.InvokeAsync(async () =>
            {
                try { await body(); }
                catch (Exception ex) { failed = ex; }
                finally { frame.Continue = false; }
            });
            Dispatcher.PushFrame(frame);
            dispatcher.InvokeShutdown();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(30))) Assert.Fail("It did not finish within thirty seconds.");
        if (failed is not null) ExceptionDispatchInfo.Capture(failed).Throw();
    }
}
