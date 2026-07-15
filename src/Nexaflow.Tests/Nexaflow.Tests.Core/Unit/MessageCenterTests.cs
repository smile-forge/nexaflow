using Nexaflow.Core.Models;
using Nexaflow.Core.Services;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit;

[TestClass]
[DoNotParallelize]   // shares the MessageCenter singleton; methods must not race each other
[CoversNode("messages")]
public class MessageCenterTests
{
    // MessageCenter is a singleton; in tests it captures the test thread's dispatcher, so its
    // CheckAccess is true here and Post/Remove run inline. Clear the shared store before each test.
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

    [TestMethod]
    public void Post_TransientMessage_ToastsButDoesNotPersistToInbox()
    {
        NotificationItem? raised = null;
        void Handler(object? s, NotificationItem m) => raised = m;
        MessageCenter.Instance.MessagePosted += Handler;
        try
        {
            // A transient offer (e.g. "Restore last session?") should toast but leave no dead entry in the
            // notifications list once it has passed.
            var transient = new NotificationItem { Title = "restore?", Transient = true };
            MessageCenter.Instance.Post(transient);

            Assert.AreSame(transient, raised, "a transient message must still raise MessagePosted (to toast)");
            Assert.AreEqual(0, MessageCenter.Instance.Messages.Count, "a transient message must not land in the inbox");
        }
        finally { MessageCenter.Instance.MessagePosted -= Handler; }
    }
}
