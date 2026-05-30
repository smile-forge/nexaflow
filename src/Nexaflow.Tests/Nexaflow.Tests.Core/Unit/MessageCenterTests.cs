using Nexaflow.Core.Models;
using Nexaflow.Core.Services;

namespace Nexaflow.Tests.Core.Unit;

[TestClass]
[DoNotParallelize]   // shares the MessageCenter singleton; methods must not race each other
public class MessageCenterTests
{
    // MessageCenter is a singleton; Application.Current is null in tests so Post/Remove run inline.
    // Clear the shared store before each test for isolation.
    [TestInitialize]
    public void Reset() => MessageCenter.Instance.Messages.Clear();

    [TestMethod]
    public void Post_InsertsNewestFirst_AndRaisesEvent()
    {
        NotificationItem? raised = null;
        void Handler(object? s, NotificationItem m) => raised = m;
        MessageCenter.Instance.MessagePosted += Handler;
        try
        {
            var first  = new NotificationItem { Title = "first" };
            var second = new NotificationItem { Title = "second" };
            MessageCenter.Instance.Post(first);
            MessageCenter.Instance.Post(second);

            Assert.AreEqual(2, MessageCenter.Instance.Messages.Count);
            Assert.AreSame(second, MessageCenter.Instance.Messages[0]);   // newest first
            Assert.AreSame(second, raised);
        }
        finally { MessageCenter.Instance.MessagePosted -= Handler; }
    }

    [TestMethod]
    public void PendingToasts_OnlyToastWorthyAndNotYetShown()
    {
        var inboxOnly = new NotificationItem { Title = "inbox", ShowToast = false };
        var pending   = new NotificationItem { Title = "pending", ShowToast = true };
        var alreadyShown = new NotificationItem { Title = "shown", ShowToast = true, ShownAsToast = true };

        MessageCenter.Instance.Post(inboxOnly);
        MessageCenter.Instance.Post(pending);
        MessageCenter.Instance.Post(alreadyShown);

        var toasts = MessageCenter.Instance.PendingToasts.ToList();

        CollectionAssert.AreEquivalent(new[] { pending }, toasts);
    }

    [TestMethod]
    public void Remove_TakesMessageOutOfTheStore()
    {
        var m = new NotificationItem { Title = "x" };
        MessageCenter.Instance.Post(m);
        Assert.AreEqual(1, MessageCenter.Instance.Messages.Count);

        MessageCenter.Instance.Remove(m);

        Assert.AreEqual(0, MessageCenter.Instance.Messages.Count);
    }
}
