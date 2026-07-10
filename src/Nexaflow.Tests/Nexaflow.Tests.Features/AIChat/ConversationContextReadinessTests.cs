using System.ComponentModel;
using System.Runtime.ExceptionServices;
using System.Windows.Controls;
using NSubstitute;
using Nexaflow.Features.AIChat;
using Nexaflow.Features.AIChat.ViewModels;
using Nexaflow.Features.Common;
using Nexaflow.Tests.Fixtures;
using Page = Nexaflow.Features.Common.Page;   // disambiguate from System.Windows.Controls.Page

namespace Nexaflow.Tests.Features.AIChat;

/// <summary>
/// The conversation's <see cref="ConversationViewModel.IsContextReady"/> aggregates the readiness of
/// every pinned context item, and must re-raise when a pinned page finishes gathering — that's the
/// signal the shell's held send waits on.
/// </summary>
[TestClass]
[CoversNode("aichat-context-pinning")]
public class ConversationContextReadinessTests
{
    // ── Fakes (hand-rolled INPC so the test project needs no MVVM source generator) ──

    private sealed class FakeContextVm : IPageViewModel, INotifyPropertyChanged
    {
        private bool _ready;
        public bool Ready
        {
            get => _ready;
            set
            {
                if (_ready == value) return;
                _ready = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsContextReady)));
            }
        }

        public string GetContext() => "fake context";
        public bool IsContextReady => _ready;
        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private sealed class FakePageView : UserControl, IPageView
    {
        public IPageViewModel? ViewModel { get; init; }
    }

    private static Page PinnedPage(IPageViewModel vm)
    {
        var page = new Page { PageKind = "Fake", Title = "Fake", ContentFactory = () => new FakePageView { ViewModel = vm } };
        page.GetOrCreateContent();   // realize Content so the aggregate can read its ViewModel
        return page;
    }

    private static ConversationViewModel NewConversation()
        => new(Substitute.For<IAIService>(), Substitute.For<IShellServices>(), new AiChatConfig(), new Page());

    // Pinning realizes a WPF UserControl (Page.Content), which must be built on an STA thread. The whole
    // body runs there so the view-models live on the same thread that raises their PropertyChanged.
    private static void Sta(Action body)
    {
        Exception? error = null;
        var t = new Thread(() =>
        {
            try { body(); }
            catch (Exception ex) { error = ex; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error is not null) ExceptionDispatchInfo.Throw(error);
    }

    // ── Tests ──────────────────────────────────────────────────────────────

    [TestMethod]
    public void IsContextReady_NoPinnedItems_True()
    {
        Assert.IsTrue(NewConversation().IsContextReady);
    }

    [TestMethod]
    public void IsContextReady_PinnedItemStillGathering_False() => Sta(() =>
    {
        var convo = NewConversation();
        convo.AddContextItem(PinnedPage(new FakeContextVm { Ready = false }));

        Assert.IsFalse(convo.IsContextReady);
    });

    [TestMethod]
    public void IsContextReady_AllPinnedReady_True() => Sta(() =>
    {
        var convo = NewConversation();
        convo.AddContextItem(PinnedPage(new FakeContextVm { Ready = true }));
        convo.AddContextItem(PinnedPage(new FakeContextVm { Ready = true }));

        Assert.IsTrue(convo.IsContextReady);
    });

    [TestMethod]
    public void IsContextReady_OneOfManyNotReady_False() => Sta(() =>
    {
        var convo = NewConversation();
        convo.AddContextItem(PinnedPage(new FakeContextVm { Ready = true }));
        convo.AddContextItem(PinnedPage(new FakeContextVm { Ready = false }));

        Assert.IsFalse(convo.IsContextReady);
    });

    [TestMethod]
    public void PinnedItemBecomingReady_FlipsAggregate_AndRaisesPropertyChanged() => Sta(() =>
    {
        // The wake-the-held-send path: a pinned page finishing its gather re-raises the conversation's
        // IsContextReady (via the live risk/readiness subscription) so the shell's await resumes.
        var convo = NewConversation();
        var child = new FakeContextVm { Ready = false };
        convo.AddContextItem(PinnedPage(child));

        var raised = false;
        convo.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ConversationViewModel.IsContextReady)) raised = true;
        };

        child.Ready = true;

        Assert.IsTrue(convo.IsContextReady);
        Assert.IsTrue(raised);
    });
}
