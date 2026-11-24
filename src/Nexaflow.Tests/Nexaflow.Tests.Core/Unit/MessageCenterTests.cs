using Nexaflow.Core.Models;
using Nexaflow.Core.Services;
using Nexaflow.Features.Common;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit;

[TestClass]

[CoversNode("messages")]
public class MessageCenterTests
{
    // Each test has an inbox of its own, on the test thread: its posts run inline.
    private MessageCenter _center = null!;

    [TestInitialize]
    public void Reset() => _center = new MessageCenter(new WpfUiThread(System.Windows.Threading.Dispatcher.CurrentDispatcher));

    [TestMethod]
    public void Post_InsertsNewestFirst_AndRaisesEvent()
    {
        NotificationItem? raised = null;
        void Handler(object? s, NotificationItem m) => raised = m;
        _center.MessagePosted += Handler;
        try
        {
            var first  = new NotificationItem { Title = "first" };
            var second = new NotificationItem { Title = "second" };
            _center.Post(first);
            _center.Post(second);

            Assert.AreEqual(2, _center.Messages.Count);
            Assert.AreSame(second, _center.Messages[0]);   // newest first
            Assert.AreSame(second, raised);
        }
        finally { _center.MessagePosted -= Handler; }
    }

    [TestMethod]
    public void AMessageAWorkspaceRaises_SaysWhichWorkspace_AndOneOfTheWholeAppDoesNot()
    {
        // One person, one inbox: every window shows every message, and each says which context it is about.
        var workspace = new Workspace { Name = "Community" };
        IShellServices shell = TestRuntimes.FactoryWith(tasks: null, notify: _center.Post).CreateUnstarted(workspace).ShellServices;

        shell.ShowError("the post did not go out");
        Assert.AreSame(workspace, _center.Messages[0].Workspace);

        _center.Post(new NotificationItem { Title = "An update is ready" });
        Assert.IsNull(_center.Messages[0].Workspace);
    }

    [TestMethod]
    public void PendingToasts_OnlyToastWorthyAndNotYetShown()
    {
        var inboxOnly = new NotificationItem { Title = "inbox", ShowToast = false };
        var pending   = new NotificationItem { Title = "pending", ShowToast = true };
        var alreadyShown = new NotificationItem { Title = "shown", ShowToast = true, ShownAsToast = true };

        _center.Post(inboxOnly);
        _center.Post(pending);
        _center.Post(alreadyShown);

        var toasts = _center.PendingToasts.ToList();

        CollectionAssert.AreEquivalent(new[] { pending }, toasts);
    }

    [TestMethod]
    public void Remove_TakesMessageOutOfTheStore()
    {
        var m = new NotificationItem { Title = "x" };
        _center.Post(m);
        Assert.AreEqual(1, _center.Messages.Count);

        _center.Remove(m);

        Assert.AreEqual(0, _center.Messages.Count);
    }

    [TestMethod]
    public void Post_TransientMessage_ToastsButDoesNotPersistToInbox()
    {
        NotificationItem? raised = null;
        void Handler(object? s, NotificationItem m) => raised = m;
        _center.MessagePosted += Handler;
        try
        {
            // A transient offer (e.g. "Restore last session?") should toast but leave no dead entry in the
            // notifications list once it has passed.
            var transient = new NotificationItem { Title = "restore?", Transient = true };
            _center.Post(transient);

            Assert.AreSame(transient, raised, "a transient message must still raise MessagePosted (to toast)");
            Assert.AreEqual(0, _center.Messages.Count, "a transient message must not land in the inbox");
        }
        finally { _center.MessagePosted -= Handler; }
    }
}
