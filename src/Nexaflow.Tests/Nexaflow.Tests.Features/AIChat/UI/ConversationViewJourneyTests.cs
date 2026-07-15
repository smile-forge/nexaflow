using System.Diagnostics;
using System.IO;
using System.Text.Json;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using Nexaflow.Tests.Features.UI.Infrastructure;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.AIChat.UI;

/// <summary>
/// Drives the real shell to a seeded conversation and back: this is the check the ViewModel unit tests
/// can't give — that the reworked <c>ConversationView</c> XAML actually loads and binds (the user-bubble
/// timestamp + rewind template, the context-preview column, the approval explanation row). The base class
/// fails the test on any unhandled UI-thread exception, so a broken binding or a missing resource key
/// surfaces here rather than silently in the app.
///
/// Interactive desktop only — run with --filter "TestCategory=UI".
/// </summary>
[TestClass]
[CoversNode("aichat-conversation-view")]
public class ConversationViewJourneyTests : UITestBase
{
    // Land straight on the conversation browser so the seeded row is one click from open.
    protected override string? LaunchTabKind => "AIChat";

    private const string ConversationId = "uitest-convo-1";

    /// <summary>Seeds a two-message conversation into the default workspace's store, so the browser lists a
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
    public void SeededConversation_OpensAndRendersTheThreadWithRewind()
    {
        // The browser lists the seeded row; open it. "Open" matches the label inside the button, so click
        // it directly (the TextBlock exposes no Invoke pattern).
        var openLabel = WaitForName("Open", 15);
        Assert.IsNotNull(openLabel, "The seeded conversation row (and its Open button) did not appear.");
        openLabel!.Click();
        Wait.UntilInputIsProcessed();

        // The conversation view loaded (its banner label is a plain TextBlock, unlike the message text,
        // which lives in a RichTextBox and exposes no accessible name).
        Assert.IsNotNull(WaitForName("Context Items", 12),
            "The conversation view did not open (its 'Context Items' banner never appeared).");

        // The newest user message offers Rewind — the template that used not to exist. Rendering it
        // end-to-end is the point: a broken binding in it would have thrown on the UI thread.
        Assert.IsNotNull(WaitForName("↺ Rewind", 8),
            "The newest user message did not show the Rewind affordance.");

        Assert.IsFalse(App.HasExited, "The app crashed while rendering the conversation view.");
        // The base-class teardown fails the test if any binding/resource error hit the UI thread.
    }

    /// <summary>Waits for a descendant with the given accessible name (UITestBase has no such helper).</summary>
    private AutomationElement? WaitForName(string name, int seconds)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromSeconds(seconds))
        {
            AutomationElement? el = null;
            try { el = MainWindow.FindFirstDescendant(cf => cf.ByName(name)); } catch { /* tree churned */ }
            if (el is not null && !el.IsOffscreen) return el;
            System.Threading.Thread.Sleep(150);
        }
        return null;
    }
}
