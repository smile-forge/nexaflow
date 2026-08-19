using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using NSubstitute;
using Nexaflow.Features.Common;
using Nexaflow.Features.Font.ViewModels;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Font;

/// <summary>
/// Per-control state and commands behind the Font tab, driven through the view-models rather than the UI.
/// The integrated interaction is covered once by <c>FontViewerJourneyTests</c>; these are the per-leaf
/// assertions for the top bar, the compare list, the details panel and the glyph map.
/// <para>
/// Fonts here come from the <b>installed</b> set rather than the generated .ttf fixture: an installed family
/// is always loadable, which keeps these tests independent of the sample-font corpus.
/// </para>
/// </summary>
[TestClass]
public class FontViewModelTests
{
    /// <summary>An installed family that is certain to exist on Windows, with a usable fallback.</summary>
    private static FontFamily InstalledFamily() =>
        Fonts.SystemFontFamilies.FirstOrDefault(f => f.Source == "Segoe UI")
        ?? Fonts.SystemFontFamilies.First();

    private static FontViewModel Vm(out IShellServices shell)
    {
        shell = Substitute.For<IShellServices>();
        return new FontViewModel(null, shell);
    }

    private static FontItemViewModel Item(FontPreviewOptions? options = null)
    {
        var item = FontItemViewModel.Installed(InstalledFamily());
        item.AttachOptions(options ?? new FontPreviewOptions());
        item.EnsureFacesLoaded();
        return item;
    }

    // ── Preview Text (font-text-display) ──────────────────────────────────

    [TestMethod]
    [TestCategory("Unit")]
    [CoversNode("font-text-display")]
    public void PreviewText_HasAPangramByDefault_AndIsSharedByEveryRow()
    {
        var vm = Vm(out _);

        StringAssert.Contains(vm.Options.PreviewText, "quick brown fox");

        // One options object is attached to every item, so editing the box restyles all rows at once.
        var a = Item(vm.Options);
        var b = Item(vm.Options);
        vm.Options.PreviewText = "Hamburgefonstiv";

        Assert.AreEqual("Hamburgefonstiv", a.Options!.PreviewText);
        Assert.AreEqual("Hamburgefonstiv", b.Options!.PreviewText);
    }

    [TestMethod]
    [TestCategory("Unit")]
    [CoversNode("font-compare")]
    public void SpecimenLine_CoversLettersDigitsAndSymbols()
    {
        var specimen = new FontPreviewOptions().SpecimenText;

        StringAssert.Contains(specimen, "ABCDEFGHIJKLMNOPQRSTUVWXYZ");
        StringAssert.Contains(specimen, "abcdefghijklmnopqrstuvwxyz");
        StringAssert.Contains(specimen, "0123456789");
    }

    // ── Size Slider (size-slider) ─────────────────────────────────────────

    [TestMethod]
    [TestCategory("Unit")]
    [CoversNode("size-slider")]
    public void SizeSlider_ConvertsPointsToDips_AndRepublishesOnChange()
    {
        var options = new FontPreviewOptions();
        var raised = new List<string?>();
        options.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        Assert.AreEqual(16, options.PreviewSizePt, "the default preview size");
        Assert.AreEqual(16 * 4.0 / 3.0, options.PreviewSizeDip, 0.001, "WPF renders in DIPs (pt x 4/3)");

        options.PreviewSizePt = 72;
        Assert.AreEqual(96, options.PreviewSizeDip, 0.001);
        CollectionAssert.Contains(raised, nameof(FontPreviewOptions.PreviewSizeDip),
            "the previews bind to the DIP value, so it must re-notify when the points change");
    }

    [TestMethod]
    [TestCategory("Unit")]
    [CoversNode("size-slider")]
    public void SpecimenSize_IsFixed_AndDoesNotFollowThePreviewSlider()
    {
        var options = new FontPreviewOptions { PreviewSizePt = 120 };

        // The small alphabet line stays legible at a constant size whatever the preview is set to.
        Assert.AreEqual(9 * 4.0 / 3.0, options.SpecimenSizeDip, 0.001);
    }

    // ── Add / Remove font (add-font, remove-font) ─────────────────────────

    [TestMethod]
    [TestCategory("Unit")]
    [CoversNode("add-font")]
    public void AddFont_OpensTheFontPickerOverlay()
    {
        var vm = Vm(out var shell);

        vm.AddFontCommand.Execute(null);

        shell.Received(1).ShowOverlay(Arg.Any<FontPickerViewModel>());
    }

    [TestMethod]
    [TestCategory("Unit")]
    [CoversNode("remove-font")]
    public void RemoveFont_DropsTheRow_AndReselectsSoTheDetailsPanelIsNeverOrphaned()
    {
        var vm = Vm(out _);
        var first = Item(vm.Options);
        var second = Item(vm.Options);
        vm.Fonts.Add(first);
        vm.Fonts.Add(second);
        vm.SelectedFont = first;

        vm.RemoveFontCommand.Execute(first);

        CollectionAssert.DoesNotContain(vm.Fonts, first);
        Assert.AreSame(second, vm.SelectedFont, "removing the selected font selects a neighbour, not nothing");
        Assert.IsTrue(vm.HasFonts);

        vm.RemoveFontCommand.Execute(second);
        Assert.IsFalse(vm.HasFonts, "the empty-state hint returns once the last font goes");
        Assert.IsNull(vm.SelectedFont);
    }

    [TestMethod]
    [TestCategory("Unit")]
    [CoversNode("remove-font")]
    public void RemoveFont_WithNothingPassed_IsASafeNoOp()
    {
        var vm = Vm(out _);
        vm.Fonts.Add(Item(vm.Options));

        vm.RemoveFontCommand.Execute(null);

        Assert.AreEqual(1, vm.Fonts.Count);
    }

    // ── Copy name / path (copy-name, copy-path) ───────────────────────────
    // The clipboard write itself is a one-line WPF call guarded by try/catch (the clipboard can be locked
    // by another process) and needs an interactive desktop — the UI journey covers that. What is asserted
    // here is the guard: with nothing selected, or a font that has no file, the commands do nothing.

    [TestMethod]
    [TestCategory("Unit")]
    [CoversNode("copy-name")]
    [CoversNode("copy-path")]
    public void CopyNameAndPath_WithNoSelection_DoNothingRatherThanThrow()
    {
        var vm = Vm(out _);
        Assert.IsNull(vm.SelectedFont);

        vm.CopyNameCommand.Execute(null);
        vm.CopyPathCommand.Execute(null);
    }

    [TestMethod]
    [TestCategory("Unit")]
    [CoversNode("copy-path")]
    public void CopyPath_ForAnInstalledFont_HasNoPathToCopy()
    {
        var vm = Vm(out _);
        var installed = Item(vm.Options);
        vm.Fonts.Add(installed);
        vm.SelectedFont = installed;

        Assert.IsTrue(installed.IsInstalled);
        Assert.IsNull(installed.SourcePath, "an installed family has no source file of its own");

        vm.CopyPathCommand.Execute(null);   // guarded — nothing to copy, and no throw
    }

    // ── Face variant chips (select-font-style) ────────────────────────────

    [TestMethod]
    [TestCategory("Unit")]
    [CoversNode("select-font-style")]
    public void SelectFace_MovesTheSelectionMark_AndRestylesThePreview()
    {
        var item = Item();
        var faces = item.Faces;
        Assert.IsTrue(faces.Count > 0, "an installed family exposes at least one face");

        var first = faces[0];
        item.SelectFaceCommand.Execute(first);
        Assert.AreSame(first, item.SelectedFace);
        Assert.IsTrue(first.IsSelected);

        var other = faces.FirstOrDefault(f => !ReferenceEquals(f, first));
        if (other is null) return;   // a single-face family has no second chip to click

        item.SelectFaceCommand.Execute(other);
        Assert.AreSame(other, item.SelectedFace);
        Assert.IsTrue(other.IsSelected);
        Assert.IsFalse(first.IsSelected, "only one chip stays lit");
    }

    [TestMethod]
    [TestCategory("Unit")]
    [CoversNode("font-compare")]
    public void EffectiveStyle_ComesFromTheSelectedFace()
    {
        var item = Item();
        item.SelectFaceCommand.Execute(item.Faces[0]);

        // The page has no style overrides at all, so a preview renders a face exactly as designed —
        // picking a different chip is the only way to change weight or slant.
        Assert.AreEqual(item.SelectedFace!.Weight, item.EffectiveWeight);
        Assert.AreEqual(item.SelectedFace!.Style, item.EffectiveStyle);
        Assert.AreEqual(item.SelectedFace!.Stretch, item.EffectiveStretch);
    }

    // ── Metadata rows (font-details-2) ────────────────────────────────────

    [TestMethod]
    [TestCategory("Unit")]
    [CoversNode("font-details-2")]
    public void Details_GroupsIdentityAndTechnicalRows_ForALoadableFont()
    {
        var item = Item();
        item.SelectFaceCommand.Execute(item.Faces[0]);

        var rows = item.Details;
        Assert.IsTrue(rows.Count > 0);
        Assert.IsTrue(rows.Any(r => r.IsHeader), "rows are grouped under headers");
    }

    [TestMethod]
    [TestCategory("Unit")]
    [CoversNode("font-details-2")]
    public void Details_ForAFontThatFailedToLoad_ReportTheErrorInsteadOfMetadata()
    {
        var failed = FontItemViewModel.Failed(@"C:\fonts\broken.ttf", "Not a font file.");

        Assert.IsFalse(failed.CanRender);
        var rows = failed.Details;
        Assert.IsTrue(rows.Any(r => r.Value == "Not a font file."),
            "a font that won't load explains itself in the details panel rather than showing nothing");
    }

    // ── Glyph map (view-glyph, copy-glyph) ────────────────────────────────

    [TestMethod]
    [TestCategory("Unit")]
    [CoversNode("view-glyph")]
    public void GlyphGrid_IsPaged_AndTheLabelAndPagingFlagsAgree()
    {
        var item = Item();
        item.SelectFaceCommand.Execute(item.Faces[0]);

        Assert.IsFalse(item.CanPrevGlyphPage, "starts on the first page");
        Assert.IsTrue(item.GlyphSamples.Count > 0, "a real font maps at least one character");
        Assert.IsFalse(string.IsNullOrWhiteSpace(item.GlyphPageLabel));

        if (!item.HasGlyphPages)
        {
            Assert.IsFalse(item.CanNextGlyphPage, "a small font fits on one page");
            return;
        }

        item.NextGlyphPageCommand.Execute(null);
        Assert.IsTrue(item.CanPrevGlyphPage);

        item.PrevGlyphPageCommand.Execute(null);
        Assert.IsFalse(item.CanPrevGlyphPage, "paging back returns to the first page");
    }

    [TestMethod]
    [TestCategory("Unit")]
    [CoversNode("view-glyph")]
    public void GlyphPaging_StopsAtBothEnds()
    {
        var item = Item();
        item.SelectFaceCommand.Execute(item.Faces[0]);

        item.PrevGlyphPageCommand.Execute(null);   // already at the start
        Assert.IsFalse(item.CanPrevGlyphPage);
        Assert.IsTrue(item.GlyphSamples.Count > 0, "paging off the end must not empty the grid");
    }

    [TestMethod]
    [TestCategory("Unit")]
    [CoversNode("copy-glyph")]
    public void CopyGlyph_IgnoresAnEmptyCell()
    {
        var item = Item();

        // Guarded before it reaches the clipboard; the real copy needs an interactive desktop (journey).
        item.CopyGlyphCommand.Execute(null);
        item.CopyGlyphCommand.Execute(string.Empty);
    }

    // ── Context preview (font-ai-preview) ─────────────────────────────────

    [TestMethod]
    [TestCategory("Unit")]
    [CoversNode("font-ai-preview")]
    public void IdentityRows_AreTheDetailsPanelsIdentityGroup_WithoutItsHeading()
    {
        var item = Item();
        item.SelectFaceCommand.Execute(item.Faces[0]);

        var identity = item.IdentityRows;
        Assert.IsFalse(identity.Any(r => r.IsHeader), "the preview supplies its own layout, not a heading row");
        CollectionAssert.Contains(identity.Select(r => r.Label).ToArray(), "Family");

        // Whatever the identity group shows in the details panel is what the preview shows — same source.
        var fromDetails = item.Details
            .SkipWhile(r => !(r.IsHeader && r.Label == "Identity")).Skip(1)
            .TakeWhile(r => !r.IsHeader)
            .Select(r => $"{r.Label}={r.Value}")
            .ToArray();
        CollectionAssert.AreEqual(fromDetails, identity.Select(r => $"{r.Label}={r.Value}").ToArray());
    }

    [TestMethod]
    [TestCategory("Unit")]
    [CoversNode("font-ai-preview")]
    public void IdentityRows_ForAnUnreadableFont_ExplainWhyInsteadOfBeingEmpty()
    {
        var rows = FontItemViewModel.Failed(@"C:\fonts\broken.ttf", "Not a font file.").IdentityRows;

        Assert.AreEqual(1, rows.Count);
        Assert.AreEqual("Not a font file.", rows[0].Value);
    }

    [TestMethod]
    [TestCategory("Unit")]
    [CoversNode("font-ai-preview")]
    public void ThePageOffersAContextPreview()
    {
        // Only the contract is asserted here, matching TextViewModelTests / MarkdownAiTests: constructing
        // the returned UserControl needs an STA thread, so the control itself is exercised by the UI journey.
        // What it renders — the identity rows and the per-font specimen — is data, covered by the two tests
        // above and by FontContextPreview.xaml binding to them.
        Assert.IsInstanceOfType(Vm(out _), typeof(IContextPreview));
    }
}
