using System.Linq;
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

    private sealed class RiskyVm(ContextSecurityRisk risk) : IPageViewModel
    {
        public string GetContext() => "fake context";
        public ContextSecurityRisk GetContextSecurityRisk() => risk;
    }

    private static Page RiskyPageOf(string kind, string id, ContextSecurityRisk risk)
    {
        var page = new Page
        {
            PageKind       = kind,
            Title          = kind,
            PageParams     = new Dictionary<string, string> { ["id"] = id },
            ContentFactory = () => new FakePageView { ViewModel = new RiskyVm(risk) },
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

    // ── Open-tabs menu ────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("aichat-context-menu")]
    public void AvailableOpenTabs_ExcludesThisConversationAndWhatIsAlreadyPinned() => Sta(() =>
    {
        // The menu is rebuilt on every open, so a tab pinned a moment ago must be gone from it — and the
        // conversation must never offer itself (AddContextItem would refuse it anyway, silently).
        var owner = PageOf("AIChat", "self");
        var one   = PageOf("Fake", "a");
        var two   = PageOf("Fake", "b");

        var shell = Shell();
        shell.GetOpenTabs().Returns([owner, one, two]);
        var convo = new ConversationViewModel(Substitute.For<IAIService>(), shell, new AiChatConfig(), owner);

        CollectionAssert.AreEquivalent(new[] { one, two }, convo.AvailableOpenTabs.ToArray(),
            "the conversation's own tab is not a context source");

        convo.AddOpenTabCommand.Execute(one);

        CollectionAssert.AreEquivalent(new[] { two }, convo.AvailableOpenTabs.ToArray(),
            "an already-pinned tab drops out of the menu — offering it again is a dead entry");
    });

    [TestMethod]
    [CoversNode("aichat-context-menu")]
    public void AddOpenTab_PinsTheTabWithoutTakingOwnershipOfIt() => Sta(() =>
    {
        // The tab strip owns an open tab. Unpinning it must not close it — unlike a page the menu
        // *created* (AddContextPage), which this conversation owns and closes on removal.
        var tab = PageOf("Fake", "a");
        var closed = false;
        tab.Closed += (_, _) => closed = true;

        var shell = Shell();
        shell.GetOpenTabs().Returns([tab]);
        var convo = new ConversationViewModel(Substitute.For<IAIService>(), shell, new AiChatConfig(), new Page());

        convo.AddOpenTabCommand.Execute(tab);
        Assert.AreEqual(1, convo.ContextItems.Count);

        convo.RemoveContextItem(tab);
        Assert.AreEqual(0, convo.ContextItems.Count);
        Assert.IsFalse(closed, "unpinning an open tab closed it — the strip, not the conversation, owns it");
    });

    // ── Collapsed summary ─────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("aichat-context-collapse")]
    public void CollapsedContext_NamesThreeThenCounts_AcrossPagesAndAttachments() => Sta(() =>
    {
        // The collapsed row is one line, so it names a few and counts the rest. Attachments are pinned
        // context too — summarising only the pages would under-report what the model can see.
        var convo = NewConversation();
        convo.AddContextItem(PageOf("Fake", "a"));
        convo.AddContextItem(PageOf("Fake", "b"));
        convo.AddAttachment(@"C:\tmp\notes.txt");
        convo.AddAttachment(@"C:\tmp\data.csv");

        CollectionAssert.AreEqual(new[] { "Fake", "Fake", "notes.txt" },
            convo.CollapsedContext.Select(e => e.Title).ToArray(),
            "the summary names the first three, pages before attachments");

        Assert.IsTrue(convo.HasCollapsedOverflow);
        Assert.AreEqual("and 1 more", convo.CollapsedContextOverflow);
        Assert.IsFalse(convo.HasNoContext);
    });


    [TestMethod]
    [CoversNode("aichat-context-collapse")]
    public void CollapsedContext_CarriesTheSecurityRisk_SoTheBadgeSurvivesCollapsing() => Sta(() =>
    {
        // Collapsing hides the detail, not the warning: a high-risk scope stays flagged in the summary
        // pill. The risk resolves in TrackRisk, *after* the collection change — so a summary built only
        // on collection change would show a freshly pinned page unbadged.
        var convo = NewConversation();
        convo.AddContextItem(RiskyPageOf("Registry", "hklm", ContextSecurityRisk.High));
        convo.AddAttachment(@"C:\tmp\notes.txt");

        CollectionAssert.AreEqual(
            new[] { ContextSecurityRisk.High, ContextSecurityRisk.Low },
            convo.CollapsedContext.Select(e => e.Risk).ToArray(),
            "the page keeps its risk; an attachment has no scope behind it to rate");
    });
    [TestMethod]
    [CoversNode("aichat-context-collapse")]
    public void CollapsedContext_UnderTheCap_CountsNothing() => Sta(() =>
    {
        var convo = NewConversation();
        convo.AddContextItem(PageOf("Fake", "a"));

        Assert.AreEqual(1, convo.CollapsedContext.Count);
        Assert.IsFalse(convo.HasCollapsedOverflow, "three or fewer is the whole list — nothing to count");
        Assert.AreEqual(string.Empty, convo.CollapsedContextOverflow);
    });

    [TestMethod]
    [CoversNode("aichat-context-collapse")]
    public void HasNoContext_CoversAttachmentsToo_NotJustPinnedPages() => Sta(() =>
    {
        // Drives the "no context items" line. HasContextItems (the expanded hint) counts pages only, so
        // reusing it here would claim emptiness while an attachment is pinned.
        var convo = NewConversation();
        Assert.IsTrue(convo.HasNoContext);

        convo.AddAttachment(@"C:\tmp\notes.txt");
        Assert.IsFalse(convo.HasNoContext, "an attachment alone is still context");
    });

    [TestMethod]
    [CoversNode("aichat-context-collapse")]
    public void ToggleContextCollapsed_FlipsBothWays() => Sta(() =>
    {
        var convo = NewConversation();
        Assert.IsFalse(convo.IsContextCollapsed, "a conversation opens showing what the model can see");

        convo.ToggleContextCollapsedCommand.Execute(null);
        Assert.IsTrue(convo.IsContextCollapsed);

        convo.ToggleContextCollapsedCommand.Execute(null);
        Assert.IsFalse(convo.IsContextCollapsed);
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
