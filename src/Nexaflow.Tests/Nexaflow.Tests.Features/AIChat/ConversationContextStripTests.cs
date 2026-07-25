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
/// The context strip: what gets pinned, what doesn't get pinned twice, and what happens when the tab is
/// reactivated. Reactivation re-runs <see cref="ConversationViewModel.LoadAsync"/> (via
/// <c>IPageView.Reinitialize</c>), which used to stack a fresh copy of the saved context onto the strip
/// every single time — the reason the strip filled up with items nobody dragged in.
/// </summary>
[TestClass]
public class ConversationContextStripTests
{
    private sealed class FakeVm : IPageViewModel
    {
        public string GetContext() => "fake context";
    }

    private sealed class FakePageView : UserControl, IPageView
    {
        public IPageViewModel? ViewModel { get; init; }
    }

    private static Page PageOf(string kind, string? id = null)
    {
        var page = new Page
        {
            PageKind       = kind,
            Title          = kind,
            PageParams     = id is null ? null : new Dictionary<string, string> { ["id"] = id },
            ContentFactory = () => new FakePageView { ViewModel = new FakeVm() },
        };
        page.GetOrCreateContent();
        return page;
    }

    /// <summary>A shell whose RunOnUiAsync actually runs the action — the substitute's default swallows it,
    /// which would silently no-op every UI-marshalled path (the duplicate flash included).</summary>
    private static IShellServices Shell()
    {
        var shell = Substitute.For<IShellServices>();
        shell.RunOnUiAsync(Arg.Any<Action>())
             .Returns(ci => { ci.Arg<Action>()(); return Task.CompletedTask; });
        return shell;
    }

    private static ConversationViewModel NewConversation(IAIService? ai = null, IShellServices? shell = null)
        => new(ai ?? Substitute.For<IAIService>(), shell ?? Shell(), new AiChatConfig(), new Page());

    // Pinning realizes a WPF UserControl, which must happen on an STA thread.
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

    // ── Duplicate handling ────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("aichat-context-chips")]
    public void AddContextItem_SamePageObjectTwice_PinnedOnce() => Sta(() =>
    {
        var convo = NewConversation();
        var page  = PageOf("Fake", "a");

        convo.AddContextItem(page);
        convo.AddContextItem(page);

        Assert.AreEqual(1, convo.ContextItems.Count);
    });

    [TestMethod]
    [CoversNode("aichat-context-chips")]
    public void HasContextItems_TracksTheStrip_DrivingTheEmptyStateHint() => Sta(() =>
    {
        // The "drag tabs or files here" hint binds to this — it used to bind Count through a bool converter
        // (always Visible), so it never cleared once an item was pinned.
        var convo = NewConversation();
        Assert.IsFalse(convo.HasContextItems, "no items → the hint shows");

        var page = PageOf("Fake", "a");
        convo.AddContextItem(page);
        Assert.IsTrue(convo.HasContextItems, "an item is pinned → the hint hides");

        convo.RemoveContextItem(page);
        Assert.IsFalse(convo.HasContextItems, "back to empty → the hint shows again");
    });

    [TestMethod]
    [CoversNode("aichat-context-chips")]
    public void AddContextItem_DifferentObjectSameKindAndParams_PinnedOnce() => Sta(() =>
    {
        // This is the restore case: RestoreContextPages rebuilds saved context as *new* Page objects, so
        // reference equality calls them new every time. Identity is kind + params.
        var convo = NewConversation();

        convo.AddContextItem(PageOf("Fake", "a"));
        convo.AddContextItem(PageOf("Fake", "a"));

        Assert.AreEqual(1, convo.ContextItems.Count);
    });

    [TestMethod]
    [CoversNode("aichat-context-chips")]
    public void AddContextItem_SameKindDifferentParams_BothPinned() => Sta(() =>
    {
        // Two file-system tabs on different folders are two different contexts.
        var convo = NewConversation();

        convo.AddContextItem(PageOf("Fake", "a"));
        convo.AddContextItem(PageOf("Fake", "b"));

        Assert.AreEqual(2, convo.ContextItems.Count);
    });

    [TestMethod]
    [CoversNode("aichat-context-chips")]
    public void AddContextItem_Duplicate_FlashesTheExistingChip() => Sta(() =>
    {
        var convo = NewConversation();
        convo.AddContextItem(PageOf("Fake", "a"));
        var existing = convo.ContextItems[0];

        convo.AddContextItem(PageOf("Fake", "a"));

        // A silently-dropped duplicate reads as "nothing happened"; the pulse is the answer.
        Assert.IsTrue(existing.IsFlashing);
        Assert.AreEqual(1, convo.ContextItems.Count);
    });

    // ── Reactivation ──────────────────────────────────────────────────────

    [TestMethod]
    public async Task LoadAsync_RunTwice_DoesNotStackDuplicateContext()
    {
        ConversationViewModel? convo = null;
        Exception? error = null;

        // Async body on an STA thread: pinning realizes UserControls.
        var t = new Thread(() =>
        {
            try
            {
                var record = new ConversationRecord
                {
                    Id      = "c1",
                    Context = [new ContextRef { PageKind = "Fake", PageParams = new() { ["id"] = "a" } }],
                };

                var ai = Substitute.For<IAIService>();
                ai.LoadConversationsAsync().Returns(Task.FromResult<IEnumerable<ConversationRecord>>([record]));
                // Fresh Page objects each call — exactly what the real RestoreContextPages does.
                ai.RestoreContextPages(Arg.Any<ConversationRecord>())
                  .Returns(_ => (IReadOnlyList<Page>)[PageOf("Fake", "a")]);

                convo = NewConversation(ai);

                convo.LoadAsync("c1").GetAwaiter().GetResult();
                convo.LoadAsync("c1").GetAwaiter().GetResult();   // the reactivation that used to duplicate
            }
            catch (Exception ex) { error = ex; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error is not null) ExceptionDispatchInfo.Throw(error);

        await Task.CompletedTask;
        Assert.AreEqual(1, convo!.ContextItems.Count,
            "reactivating the tab re-restored the saved context instead of replacing it");
    }

    // ── Selection + preview ───────────────────────────────────────────────

    [TestMethod]
    [CoversNode("aichat-context-preview")]
    public void SelectContextItem_SelectsAndOpensPanel_AndTogglesOff() => Sta(() =>
    {
        var convo = NewConversation();
        convo.AddContextItem(PageOf("Fake", "a"));
        var item = convo.ContextItems[0];

        convo.SelectContextItemCommand.Execute(item);

        Assert.IsTrue(item.IsSelected);
        Assert.IsTrue(convo.IsPreviewOpen);
        Assert.AreEqual("Fake", convo.PreviewTitle);

        convo.SelectContextItemCommand.Execute(item);   // clicking the selected chip closes it

        Assert.IsFalse(item.IsSelected);
        Assert.IsFalse(convo.IsPreviewOpen);
    });

    [TestMethod]
    [CoversNode("aichat-context-preview")]
    public void SelectContextItem_OnlyOneSelectedAtATime() => Sta(() =>
    {
        var convo = NewConversation();
        convo.AddContextItem(PageOf("Fake", "a"));
        convo.AddContextItem(PageOf("Fake", "b"));

        convo.SelectContextItemCommand.Execute(convo.ContextItems[0]);
        convo.SelectContextItemCommand.Execute(convo.ContextItems[1]);

        Assert.IsFalse(convo.ContextItems[0].IsSelected);
        Assert.IsTrue(convo.ContextItems[1].IsSelected);
    });

    [TestMethod]
    [CoversNode("aichat-context-preview")]
    public void PageWithoutContextPreview_FallsBackToThePlaceholder() => Sta(() =>
    {
        // FakeVm implements IPageViewModel but not IContextPreview — no preview control, so the panel
        // shows the identity placeholder with the context text the AI sees.
        var convo = NewConversation();
        convo.AddContextItem(PageOf("Fake", "a"));

        convo.SelectContextItemCommand.Execute(convo.ContextItems[0]);

        Assert.IsTrue(convo.IsPreviewOpen);
        Assert.IsFalse(convo.HasPreviewContent);
        StringAssert.Contains(convo.PreviewFallbackText, "fake context");
    });

    [TestMethod]
    [CoversNode("aichat-context-preview")]
    public void ClosePreview_Deselects() => Sta(() =>
    {
        var convo = NewConversation();
        convo.AddContextItem(PageOf("Fake", "a"));
        convo.SelectContextItemCommand.Execute(convo.ContextItems[0]);

        convo.ClosePreviewCommand.Execute(null);

        Assert.IsFalse(convo.IsPreviewOpen);
        Assert.IsFalse(convo.ContextItems[0].IsSelected);
    });

    [TestMethod]
    [CoversNode("aichat-context-preview")]
    public void RemovingTheSelectedItem_ClosesThePanel() => Sta(() =>
    {
        var convo = NewConversation();
        var page  = PageOf("Fake", "a");
        convo.AddContextItem(page);
        convo.SelectContextItemCommand.Execute(convo.ContextItems[0]);

        convo.RemoveContextItem(page);

        Assert.IsFalse(convo.IsPreviewOpen, "the panel was left open previewing an item that is gone");
        Assert.AreEqual(0, convo.ContextItems.Count);
    });
}
