using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.UIJourneys.Infrastructure;

namespace Nexaflow.Tests.Features.Compressed.UI;

/// <summary>
/// One-pass UI journey for the archive inspector: opens a zip via the explicit <b>"As Archive"</b>
/// action, expands a folder row, and round-trips each of the four overlay-raising action-bar buttons.
/// <para>
/// Two things shape this journey and are worth stating, because getting either wrong makes it report
/// failures that are not there.
/// </para>
/// <para>
/// <b>The overlays are input-blocking.</b> Encrypt and Decrypt raise the password overlay; Recompress
/// and Convert raise the choice overlay. Both are a full-page <c>#CC000000</c> scrim at a high ZIndex,
/// so while one is up every other control on the page is unclickable — and neither closes on Escape in
/// the state a journey reaches it in. Pressing them in declaration order without dismissing would put
/// the scrim up at Encrypt and have every later press land on it, so the journey would report Sign,
/// Sig Check, Recompress and Convert as broken when they were merely covered. Each is therefore opened
/// and closed as a pair, and its Cancel button is the assertion that it closed.
/// </para>
/// <para>
/// <b>Most of the action bar mutates the archive or the disk</b>, so it is present-checked and never
/// pressed: Extract writes the entries out, Add file writes a new entry in, Sign writes a signature,
/// and the two option buttons inside the choice overlay are what actually perform a repackage — into a
/// sibling path that is never overwritten, so pressing one would leak a new file on every run. Test is
/// the exception and is pressed: it is the archive's own integrity check, and while it does write, it
/// writes only into an LRU-capped temp directory.
/// </para>
/// Interactive desktop only — run with <c>--filter "TestCategory=UI"</c>.
/// </summary>
[TestClass]
[CoversNode("compressed-actionbar")]
[CoversNode("compressed-overlays")]
public class CompressedJourneyTests : UiJourneyTestBase
{
    /// <summary>
    /// Opens and dismisses one overlay, asserting both halves — the only safe way to press these. When
    /// <paramref name="optionId"/> is given, the option button inside is asserted present while the
    /// overlay is up; it is never pressed, because picking an option performs the repackage.
    /// </summary>
    private void RoundTripOverlay(string label, string openId, string cancelId, string? optionId = null)
    {
        var opener = WaitForId(openId, 5);
        if (opener is null) { CheckPresent(label, openId); return; }   // records the absence

        // Enablement is not a property of the archive. Encrypt/Decrypt need an IArchiveEncryptor to have
        // been discovered in the feature catalog at all, so on a run where that codec assembly is absent
        // both are permanently disabled and pressing them would be recorded as a failure of the button.
        if (!opener.IsEnabled) { CheckPresent($"{label} (disabled on this run)", openId); return; }

        CheckDoes($"{label} opens its overlay", openId, () => WaitForId(cancelId, 4) is not null);
        if (optionId is not null) CheckPresent($"{label} option button", optionId);
        CheckDoes($"{label} overlay closes",    cancelId, () => WaitForId(cancelId, 3) is null);
    }

    [TestMethod]
    public void Compressed_Controls_RespondInOnePass()
    {
        // sample.zip, because the row chevron is bound to HasChildren and only exists in the tree for an
        // archive with a real folder in it — sample.zip holds docs/data.json. Not nested.zip, whose name
        // promises a tree and delivers a zip inside a zip (top.txt + inner.zip), so every row is a leaf
        // and the chevron never renders.
        var view = OpenFileVia(TestSampleData.Path("archive"), "sample.zip", "As Archive", "CompressedView");
        Assert.IsNotNull(view, "CompressedView did not open via the 'As Archive' action.");

        // ── The tree ──────────────────────────────────────────────────────────
        // One id on N controls: the chevron lives in the ListBox's ItemTemplate, so it is stamped on every
        // realized folder row and this finds whichever is first in tree order. That is enough to prove the
        // expand path works; which row it expanded is not what this journey is asserting.
        CheckInvoke("Row expand chevron", "Compressed_RowExpand");

        // ── Destructive: present, never pressed ───────────────────────────────
        CheckPresent("Extract",  "Compressed_Extract");
        CheckPresent("Add file", "Compressed_AddFile");
        CheckPresent("Sign",     "Compressed_Sign");

        // Sig Check needs the archive to carry a signature, which the fixture does not, so it is present
        // and disabled — asserted as present rather than pressed.
        CheckPresent("Sig Check", "Compressed_Verify");

        // ── The one action-bar button worth pressing ──────────────────────────
        // Writes, but only into the temp dir the VFS caches reads through. CheckInvoke already fails the
        // journey if the app exits, which is what this is really asking. The status text lands after a
        // background pass over the entries, so it is deliberately not asserted here.
        CheckInvoke("Test (integrity check)", "Compressed_Test");

        // ── Overlay round-trips ───────────────────────────────────────────────
        RoundTripOverlay("Encrypt",    "Compressed_Encrypt",    "Compressed_PasswordCancel");
        RoundTripOverlay("Decrypt",    "Compressed_Decrypt",    "Compressed_PasswordCancel");
        // Recompress and Convert raise the same overlay in different modes — a stacked list of levels and
        // a grid of formats — so their option buttons carry different ids, and each is only visible while
        // its own mode is up.
        RoundTripOverlay("Recompress", "Compressed_Recompress", "Compressed_ChoiceCancel", "Compressed_RecompressOption");
        RoundTripOverlay("Convert",    "Compressed_Convert",    "Compressed_ChoiceCancel", "Compressed_ConvertOption");

        // The password overlay's OK button commits the entered password, so it is only ever asserted
        // present — and it can only be seen while that overlay is up.
        CheckDoes("Password overlay reopens for its OK button", "Compressed_Encrypt",
                  () => WaitForId("Compressed_PasswordOk", 4) is not null);
        CheckPresent("Password OK", "Compressed_PasswordOk");
        CheckDoes("and closes again", "Compressed_PasswordCancel",
                  () => WaitForId("Compressed_PasswordCancel", 3) is null);

        AssertJourney();
    }
}
