using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using Nexaflow.Tests.UIJourneys.Infrastructure;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.AIChat.UI;

/// <summary>
/// One-pass UI journey for the AI chat browser and the conversation view behind it: seeds a transcript,
/// exercises the browser row's controls and the analysis overlay, opens the conversation, and works the
/// context banner — soft-asserting each so a single gap does not hide the rest.
/// <para>
/// It also stands in for the check the ViewModel tests cannot give: that the ConversationView XAML really
/// loads and binds (the user-bubble timestamp and rewind template, the context banner). A wrong resource
/// key or a mistyped binding path fails silently in WPF — no exception, just nothing on screen — and the
/// base class turns any UI-thread exception into a failure here.
/// </para>
/// <para>
/// Two controls are present-checked rather than pressed, both because they destroy the fixture the rest of
/// the journey needs. Delete removes the conversation's folder from disk, including the transcript seeded
/// below. Rewind writes the truncated transcript back over conversation.json, and its own doc comment says
/// the dropped turns are gone for good.
/// </para>
/// <para>
/// The conversation tab is deliberately never closed. Closing it runs <c>OnOwnerClosed</c>, which queues a
/// <c>ConversationAnalysisTask</c> for any conversation with messages — and this one has four. No provider
/// is configured in a journey's throwaway config dir so nothing would be sent, but a background task that
/// exists only to fail is not a thing to leave running under a test that fails on crash-log entries.
/// Teardown kills the process, which never raises Closed.
/// </para>
/// Interactive desktop only — run with <c>--filter "TestCategory=UI"</c>.
/// </summary>
[TestClass]
[CoversNode("aichat-conversation-view")]
public class ConversationViewJourneyTests : UiJourneyTestBase
{
    // Land straight on the conversation browser so the seeded row is one click from open.
    protected override string? LaunchTabKind => "AIChat";

    private const string ConversationId = "uitest-convo-1";

    /// <summary>Seeds a four-message conversation into the default workspace's store, so the browser lists a
    /// row we can open — no provider needed, since we only read an existing transcript.</summary>
    protected override void SeedConfig(string configDir)
    {
        var dir = Path.Combine(configDir, "Contexts", "Default", "Conversations", ConversationId);
        Directory.CreateDirectory(dir);

        var now = DateTime.Now;
        var record = new
        {
            Id        = ConversationId,
            StartedAt = now.AddMinutes(-10),
            Title     = "Seeded UI conversation",
            Messages  = new object[]
            {
                new { Id = Guid.NewGuid().ToString(), Text = "first question",  IsUser = true,  Timestamp = now.AddMinutes(-10) },
                new { Id = Guid.NewGuid().ToString(), Text = "first answer",    IsUser = false, Timestamp = now.AddMinutes(-9) },
                new { Id = Guid.NewGuid().ToString(), Text = "second question", IsUser = true,  Timestamp = now.AddMinutes(-2) },
                new { Id = Guid.NewGuid().ToString(), Text = "second answer",   IsUser = false, Timestamp = now.AddMinutes(-1) },
            },
            Attachments = Array.Empty<string>(),
            Context     = Array.Empty<object>(),
        };

        File.WriteAllText(Path.Combine(dir, "conversation.json"),
            JsonSerializer.Serialize(record, new JsonSerializerOptions { WriteIndented = true }));
    }

    [TestMethod]
    public void AiChat_Controls_RespondInOnePass()
    {
        // ── The browser ───────────────────────────────────────────────────────
        Check("the seeded conversation row appears", () => WaitForId("AiChat_OpenConversation", 15) is not null);

        // Deletes the conversation's folder — and the transcript everything below reads.
        CheckPresent("Delete conversation", "AiChat_DeleteConversation");

        // The analysis overlay is a full-screen Border spanning both rows with a hit-testable background,
        // so while it is open nothing behind it can be clicked. Opened and closed as a pair, with the
        // button that lives inside it checked while it is up. The magnifier is only offered for a row that
        // has an analysis on disk, which a freshly seeded transcript does not — hence the branch.
        var analysis = WaitForId("AiChat_ShowAnalysis", 5);
        if (analysis is { IsEnabled: true })
        {
            CheckDoes("Analysis overlay opens", "AiChat_ShowAnalysis",
                      () => WaitForId("AiChat_CloseAnalysis", 4) is not null);
            CheckPresent("Open from the analysis overlay", "AiChat_AnalysisOpenConversation");
            CheckDoes("Analysis overlay closes", "AiChat_CloseAnalysis",
                      () => WaitForId("AiChat_CloseAnalysis", 3) is null);
        }
        else
        {
            // Asserted rather than skipped silently: "this row has no analysis" is a different claim from
            // "the overlay is broken", and both ids still have to be named by a journey for the guard.
            CheckPresent("Analysis magnifier (no analysis on a fresh transcript)", "AiChat_ShowAnalysis");
            Check("the analysis overlay starts closed",
                  () => WaitForId("AiChat_CloseAnalysis", 1) is null
                     && WaitForId("AiChat_AnalysisOpenConversation", 1) is null);
        }

        // ── Into the conversation ─────────────────────────────────────────────
        CheckDoes("Open the seeded conversation", "AiChat_OpenConversation",
                  () => WaitForId("Conversation_ToggleContext", 12) is not null);

        // Rewind renders on the newest user message — the template that used not to exist. Its presence is
        // the end-to-end proof that the bubble template bound; pressing it would truncate the transcript.
        CheckPresent("Rewind on the newest user message", "Conversation_Rewind");

        // Add context opens a menu of sources. It only reads shell state, but it is a menu and has to be
        // dismissed or every later press lands on it. Unlike the archive overlays this is a real
        // ContextMenu, which does close on Escape.
        CheckDoes("Add context opens the source menu", "Conversation_AddContext",
                  () => WaitForName("Open tabs", 6) is not null);
        Keyboard.Press(VirtualKeyShort.ESCAPE);
        Wait.UntilInputIsProcessed();
        // WaitForName returns the moment it FINDS something, so asking it for null is asking whether the
        // menu had already gone on the first poll — it had not, and the menu was blamed for still closing.
        Check("the source menu closes again", () => WaitUntilNameGone("Open tabs", 5));

        // The disclosure arrow collapses the chips to a one-line summary. Chrome state, deliberately not
        // persisted — but it hides the Add-context button while collapsed, so it is flipped back after.
        CheckInvoke("Collapse the context banner", "Conversation_ToggleContext");
        CheckInvoke("Expand it again",             "Conversation_ToggleContext");
        Check("Add context is available again", () => WaitForId("Conversation_AddContext", 4) is not null);

        // ── Back to the browser for a new thread ──────────────────────────────
        // New conversation lives on the browser page, and opening the seeded conversation moved us onto a
        // tab of its own — the two are different page kinds ("AIChat" and "Conversation"), so the browser's
        // tab is unambiguous to click back to.
        CheckDoes("Return to the conversation browser", "TabItem_AIChat",
                  () => WaitForId("AiChat_NewConversation", 6) is not null);

        // The record is in-memory until a message is sent, and OnOwnerClosed bails on an empty transcript,
        // so this writes nothing and queues nothing.
        CheckInvoke("New conversation", "AiChat_NewConversation");

        AssertJourney();
    }

    /// <summary>
    /// Waits for a named descendant to go away. The counterpart of <see cref="WaitForName"/>, which
    /// returns the instant it finds something and so can never answer "is it gone yet".
    /// </summary>
    private bool WaitUntilNameGone(string name, int seconds)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromSeconds(seconds))
        {
            AutomationElement? el = null;
            try { el = MainWindow.FindFirstDescendant(cf => cf.ByName(name)); } catch { /* tree churned */ }
            if (el is null || el.IsOffscreen) return true;
            System.Threading.Thread.Sleep(150);
        }
        return false;
    }
}
