using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using Nexaflow.Features.Common;
using Nexaflow.Features.Dicom.Models;
using Nexaflow.Features.Dicom.ViewModels;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Features.Dicom;

/// <summary>
/// The DICOM viewer's controls, driven through the view-model: what the contents panel says, how selection
/// moves, the window presets, the cine transport, and the tag drawer's filter.
/// <para>
/// Two rules here are easy to get subtly wrong and impossible to spot by looking. Annotations are held
/// <i>per frame</i>, so measuring on one slice and stepping away must not carry the annotation onto a
/// different slice — nor lose it when you step back. And the wheel over the stage <b>clamps</b> at the ends
/// of a series while the cine transport <b>wraps</b>: scrolling past the last slice must not jump you back
/// to the first, because in a stack of slices that reads as anatomy that isn't there.
/// </para>
/// </summary>
[TestClass]
public class DicomSurfaceTests
{
    private static string SampleDcm => TestSampleData.Path("dicom", "ct.dcm");

    private string _tmp = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _tmp = Path.Combine(Path.GetTempPath(), "nexa-dicom-surface-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tmp);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_tmp, recursive: true); } catch { }
    }

    // ── Fixtures ──────────────────────────────────────────────────────────────

    private static DicomViewModel Open(params string[] paths)
    {
        var vm = new DicomViewModel(paths, Substitute.For<IShellServices>(),
                                    new Dictionary<string, string> { ["path"] = paths.FirstOrDefault() ?? "" });
        Settled(() => !vm.IsLoading, "the container to load");
        return vm;
    }

    /// <summary>A loaded single-image viewer whose first frame has actually been rendered — the window
    /// presets and the measurement tools are all no-ops until the renderer exists.</summary>
    private static DicomViewModel Rendered(params string[] paths)
    {
        var vm = Open(paths);
        Settled(() => vm.CurrentBitmap is not null, "the first frame to render");
        return vm;
    }

    /// <summary>Two 8-frame instances in one series — the cine transport only exists for a multi-frame
    /// image, and there has to be somewhere else to go for "opening another image stops it" to mean
    /// anything.</summary>
    private DicomViewModel MultiFrame()
    {
        for (var i = 1; i <= 2; i++)
            DicomTestFiles.WriteImage(_tmp, $"cine{i}.dcm", "PC", "Cine^Test",
                                      "1.7.1", "1.7.1.1", $"1.7.1.1.{i}", frames: 8, instanceNumber: i);
        var vm = Rendered(_tmp);
        Settled(() => vm.FrameCount == 8, "the frame count to arrive");
        return vm;
    }

    /// <summary>A three-slice series, so stepping through the stack has ends to run into.</summary>
    private DicomViewModel Series()
    {
        for (var i = 1; i <= 3; i++)
            DicomTestFiles.WriteImage(_tmp, $"slice{i}.dcm", "PS", "Stack^Test",
                                      "1.7.2", "1.7.2.1", $"1.7.2.1.{i}", instanceNumber: i);
        var vm = Rendered(_tmp);
        Settled(() => vm.Container?.Images.Count == 3, "the series to load");
        return vm;
    }

    /// <summary>The view-model does its work on background tasks it does not hand back, so waiting on the
    /// effect is the only honest way to observe it.</summary>
    private static void Settled(Func<bool> until, string what)
        => Assert.IsTrue(SpinWait.SpinUntil(until, 10000), $"timed out waiting for {what}");

    // ── Contents panel ────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("dicom-contents-status")]
    public void AnEmptyFolderSaysSo_RatherThanSittingOnLoading()
    {
        File.WriteAllText(Path.Combine(_tmp, "readme.txt"), "not a scan");
        var vm = Open(_tmp);
        try
        {
            Assert.IsFalse(vm.IsLoading);
            Assert.AreEqual("No DICOM content found.", vm.StatusText);
            Assert.IsTrue(vm.IsContextReady, "and the page is ready to be described — it is simply empty");
        }
        finally { vm.Dispose(); }
    }

    [TestMethod]
    [CoversNode("dicom-contents-status")]
    public void TheSummaryLineCarriesNoPatientIdentifiers()
    {
        var vm = Open(SampleDcm);
        try
        {
            Assert.AreEqual(vm.Container!.Summary, vm.StatusText);
            // The sample's PHI: PatientName "Doe^John", PatientID "TEST001". The header sits above the tree
            // whether or not Hide-patient is on, so it must never have been identifying to begin with.
            Assert.IsFalse(vm.StatusText.Contains("John") || vm.StatusText.Contains("TEST001"),
                           $"the always-visible header must not identify anyone: '{vm.StatusText}'");
        }
        finally { vm.Dispose(); }
    }

    // ── Content tree ──────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("dicom-content-tree")]
    public void TheFirstImageIsSelectedOnLoad_SoThePaneIsNotBlank()
    {
        var vm = Open(SampleDcm);
        try
        {
            Assert.AreSame(vm.Container!.Images[0], vm.SelectedNode);
            Assert.IsTrue(vm.HasImage, "the image tools are live straight away");
            Assert.IsTrue(vm.HasInstance, "and so is the tag drawer");
        }
        finally { vm.Dispose(); }
    }

    [TestMethod]
    [CoversNode("dicom-content-tree")]
    public void SelectingAStudyJumpsToItsFirstImage_RatherThanEmptyingThePane()
    {
        var vm = Series();
        try
        {
            var patient = vm.Container!.Patients[0];

            vm.SelectedNode = patient;

            Assert.AreEqual(DicomNodeKind.Image, vm.SelectedNode!.Kind,
                            "clicking a grouping node lands on something viewable");
            Assert.AreSame(vm.Container.Images[0], vm.SelectedNode);
        }
        finally { vm.Dispose(); }
    }

    // ── Series scroll (the wheel) ─────────────────────────────────────────────

    [TestMethod]
    [CoversNode("dicom-series-scroll")]
    public void ScrollingThroughTheStackStopsAtBothEnds_ItDoesNotWrap()
    {
        var vm = Series();
        try
        {
            var images = vm.Container!.Images;

            vm.StepImage(-1);
            Assert.AreSame(images[0], vm.SelectedNode, "scrolling up off the first slice stays on it");

            vm.StepImage(1);
            vm.StepImage(1);
            Assert.AreSame(images[2], vm.SelectedNode);

            vm.StepImage(1);
            Assert.AreSame(images[2], vm.SelectedNode,
                           "and off the last slice stays there — wrapping to the top of the stack would " +
                           "read as anatomy that isn't next to it");
        }
        finally { vm.Dispose(); }
    }

    // ── Cine ──────────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("dicom-cine-step")]
    public void SteppingFramesWrapsBothWays_BecauseACineSequenceIsALoop()
    {
        var vm = MultiFrame();
        try
        {
            Assert.IsTrue(vm.MultiFrame, "the transport is only shown for a multi-frame image");

            vm.PrevFrameCommand.Execute(null);
            Assert.AreEqual(7, vm.FrameIndex, "back from the first frame lands on the last");

            vm.NextFrameCommand.Execute(null);
            Assert.AreEqual(0, vm.FrameIndex, "and forward from the last comes round again");
        }
        finally { vm.Dispose(); }
    }

    [TestMethod]
    [CoversNode("dicom-cine-step")]
    public void SteppingASingleFrameImageDoesNothing()
    {
        var vm = Rendered(SampleDcm);
        try
        {
            Assert.IsFalse(vm.MultiFrame);
            vm.NextFrameCommand.Execute(null);
            Assert.AreEqual(0, vm.FrameIndex);
        }
        finally { vm.Dispose(); }
    }

    [TestMethod]
    [CoversNode("dicom-cine-play")]
    public void OpeningAnotherImageStopsCine_RatherThanRunningItOverTheNewOne()
    {
        var vm = MultiFrame();
        try
        {
            vm.ToggleCineCommand.Execute(null);
            Assert.IsTrue(vm.IsCine);

            vm.SelectedNode = vm.Container!.Images[1];

            Settled(() => !vm.IsCine,
                    "cine to stop — leaving it running would play the new instance the moment it loaded");
        }
        finally { vm.Dispose(); }
    }

    [TestMethod]
    [CoversNode("dicom-cine-slider")]
    public void AnnotationsFollowTheirOwnFrame_NotTheImage()
    {
        var vm = MultiFrame();
        try
        {
            vm.Measure.Commit(MeasurementTool.Length, [new Point(0, 0), new Point(3, 0)]);
            Assert.AreEqual(1, vm.Measure.Current.Count);

            vm.FrameIndex = 4;                       // what dragging the slider does
            Settled(() => vm.Measure.Current.Count == 0, "frame 4's own (empty) annotation set");

            vm.FrameIndex = 0;
            Settled(() => vm.Measure.Current.Count == 1, "frame 0's annotation to come back");
        }
        finally { vm.Dispose(); }
    }

    // ── Measurement tools ─────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("dicom-tool-picker")]
    public void EachToolAsksForTheNumberOfClicksItsShapeNeeds()
    {
        var vm = Rendered(SampleDcm);
        try
        {
            vm.SetToolCommand.Execute("Length");
            Assert.AreEqual(MeasurementTool.Length, vm.Measure.ActiveTool);
            Assert.AreEqual(2, vm.Measure.PointsNeeded);

            vm.SetToolCommand.Execute("Angle");
            Assert.AreEqual(3, vm.Measure.PointsNeeded, "an angle needs a vertex as well as two arms");

            vm.SetToolCommand.Execute("Ellipse");
            Assert.AreEqual(2, vm.Measure.PointsNeeded, "an ROI is two opposite corners");

            vm.SetToolCommand.Execute("Probe");
            Assert.AreEqual(0, vm.Measure.PointsNeeded, "the probe is a hover readout, not an annotation");
        }
        finally { vm.Dispose(); }
    }

    [TestMethod]
    [CoversNode("dicom-tool-picker")]
    public void AnUnknownToolNameFallsBackToPanning_NotToAHalfArmedTool()
    {
        var vm = Rendered(SampleDcm);
        try
        {
            vm.SetToolCommand.Execute("Length");
            vm.SetToolCommand.Execute("wibble");

            Assert.AreEqual(MeasurementTool.None, vm.Measure.ActiveTool);
            Assert.AreEqual(0, vm.Measure.PointsNeeded);
        }
        finally { vm.Dispose(); }
    }

    [TestMethod]
    [CoversNode("dicom-clear-measurements")]
    public void ClearOnlyEmptiesTheFrameYouAreLookingAt()
    {
        var vm = MultiFrame();
        try
        {
            vm.Measure.Commit(MeasurementTool.Length, [new Point(0, 0), new Point(3, 0)]);

            vm.FrameIndex = 2;
            Settled(() => vm.Measure.Current.Count == 0, "frame 2");
            vm.Measure.Commit(MeasurementTool.Length, [new Point(1, 1), new Point(2, 2)]);

            vm.ClearMeasurementsCommand.Execute(null);
            Assert.AreEqual(0, vm.Measure.Current.Count);

            vm.FrameIndex = 0;
            Settled(() => vm.Measure.Current.Count == 1, "frame 0's annotation to survive a clear on frame 2");
        }
        finally { vm.Dispose(); }
    }

    // ── Window / level ────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("dicom-wl-presets")]
    public void EachCtPresetAppliesItsStandardWindow_AndHighlightsItself()
    {
        var vm = Rendered(SampleDcm);
        try
        {
            foreach (var (preset, width, centre) in new[]
                     {
                         ("bone", 2000d, 400d), ("lung", 1500d, -600d),
                         ("soft", 400d, 40d), ("brain", 80d, 40d),
                     })
            {
                vm.ApplyWindowPresetCommand.Execute(preset);
                Assert.AreEqual(width, vm.WindowWidth, $"{preset} window width");
                Assert.AreEqual(centre, vm.WindowCenter, $"{preset} window centre");
                Assert.AreEqual(preset, vm.ActivePreset, "the toolbar shows which preset is live");
            }
        }
        finally { vm.Dispose(); }
    }

    [TestMethod]
    [CoversNode("dicom-wl-presets")]
    public void DefaultRestoresTheWindowTheImageItselfDeclared()
    {
        var vm = Rendered(SampleDcm);
        try
        {
            // The sample declares WindowWidth 256 / WindowCenter 128.
            var (nativeWidth, nativeCentre) = (vm.WindowWidth, vm.WindowCenter);
            Assert.AreEqual(256d, nativeWidth, "the image's own window is what the viewer opens on");

            vm.ApplyWindowPresetCommand.Execute("bone");
            Assert.AreNotEqual(nativeWidth, vm.WindowWidth);

            vm.ApplyWindowPresetCommand.Execute("default");

            Assert.AreEqual(nativeWidth, vm.WindowWidth, "Default is the image's window, not a fixed one");
            Assert.AreEqual(nativeCentre, vm.WindowCenter);
            Assert.AreEqual("default", vm.ActivePreset);
        }
        finally { vm.Dispose(); }
    }

    [TestMethod]
    [CoversNode("dicom-wl-presets")]
    public void AnUnknownPresetLeavesTheWindowAlone()
    {
        var vm = Rendered(SampleDcm);
        try
        {
            vm.ApplyWindowPresetCommand.Execute("bone");

            vm.ApplyWindowPresetCommand.Execute("pancreas");

            Assert.AreEqual(2000d, vm.WindowWidth, "an unrecognised key must not blank the window");
            Assert.AreEqual("bone", vm.ActivePreset);
        }
        finally { vm.Dispose(); }
    }

    [TestMethod]
    [CoversNode("dicom-window-level")]
    public void DraggingTheWindowDropsThePresetToCustom_AndNeverCollapsesTheWidth()
    {
        var vm = Rendered(SampleDcm);
        try
        {
            vm.ApplyWindowPresetCommand.Execute("bone");

            vm.NudgeWindowLevel(50, -20);
            Assert.AreEqual(2050d, vm.WindowWidth);
            Assert.AreEqual(380d, vm.WindowCenter);
            Assert.AreEqual("custom", vm.ActivePreset, "no preset button is lit once you drag by hand");

            vm.NudgeWindowLevel(-99999, 0);
            Assert.AreEqual(1d, vm.WindowWidth,
                            "a width of zero would map every stored value to one shade — floored at 1");
        }
        finally { vm.Dispose(); }
    }

    // ── Overlays ──────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("dicom-overlays")]
    public void TheTechnicalOverlayReportsModality_Geometry_AndTheLiveWindow()
    {
        var vm = Rendered(SampleDcm);
        try
        {
            vm.ApplyWindowPresetCommand.Execute("bone");
            Settled(() => vm.TechOverlay.Contains("W 2000"), "the overlay to follow the window");

            StringAssert.Contains(vm.TechOverlay, "4×4", "geometry is stated in source pixels");
            StringAssert.Contains(vm.TechOverlay, "L 400");
            Assert.IsFalse(vm.TechOverlay.Contains("frame"), "no frame counter on a single-frame image");
        }
        finally { vm.Dispose(); }
    }

    [TestMethod]
    [CoversNode("dicom-overlays")]
    public void AMultiFrameImageShowsItsPositionInTheStack()
    {
        var vm = MultiFrame();
        try
        {
            Settled(() => vm.TechOverlay.Contains("frame 1/8"), "the frame counter");

            vm.NextFrameCommand.Execute(null);
            Settled(() => vm.TechOverlay.Contains("frame 2/8"), "the counter to follow the frame");
        }
        finally { vm.Dispose(); }
    }

    [TestMethod]
    [CoversNode("dicom-overlays")]
    public void TheHidePatientToggleTakesTheIdentifyingLineOffTheImage()
    {
        var vm = Rendered(SampleDcm);
        try
        {
            Assert.IsTrue(vm.PatientOverlayVisible, "identifiers are shown by default — this is a viewer");

            vm.ToggleHidePatientInfoCommand.Execute(null);

            Assert.IsTrue(vm.HidePatientInfo);
            Assert.IsFalse(vm.PatientOverlayVisible, "one click clears it for a shoulder-surfer or a screen share");
        }
        finally { vm.Dispose(); }
    }

    // ── Tag drawer ────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("dicom-tags-toggle")]
    public void OpeningTheDrawerReadsTheSelectedInstancesTags()
    {
        var vm = Rendered(SampleDcm);
        try
        {
            Assert.AreEqual(0, vm.Tags.Count, "closed, the drawer has read nothing");

            vm.ToggleTagsCommand.Execute(null);

            Assert.IsTrue(vm.TagsOpen);
            Settled(() => vm.Tags.Count > 0, "the tag list to load");
            Assert.IsTrue(vm.Tags.Any(t => t.Name.Contains("Modality")));
        }
        finally { vm.Dispose(); }
    }

    [TestMethod]
    [CoversNode("dicom-tag-filter")]
    public void TheFilterMatchesTheTagId_TheName_OrTheValue()
    {
        var vm = Rendered(SampleDcm);
        try
        {
            vm.ToggleTagsCommand.Execute(null);
            Settled(() => vm.Tags.Count > 0, "the tag list to load");
            var all = vm.Tags.Count;

            vm.TagFilter = "Modality";
            Assert.IsTrue(vm.Tags.Count > 0 && vm.Tags.Count < all, "by name");

            vm.TagFilter = "0008";
            Assert.IsTrue(vm.Tags.All(t => t.Tag.Contains("0008")), "by tag id — how a spec is read");

            vm.TagFilter = "MONOCHROME2";
            Assert.IsTrue(vm.Tags.Count > 0, "by value — how you find which tag says a thing");

            vm.TagFilter = "";
            Assert.AreEqual(all, vm.Tags.Count, "clearing the box brings the whole list back");
        }
        finally { vm.Dispose(); }
    }

    [TestMethod]
    [CoversNode("dicom-tag-list")]
    public void HidingPatientInfoRereadsTheDrawer_SoTheValuesMaskInPlace()
    {
        var vm = Rendered(SampleDcm);
        try
        {
            vm.ToggleTagsCommand.Execute(null);
            Settled(() => vm.Tags.Any(t => t.Value.Contains("John")), "the unmasked patient name");

            vm.HidePatientInfo = true;

            Settled(() => !vm.Tags.Any(t => t.Value.Contains("John")),
                    "the open drawer to mask its identifying values rather than needing a reopen");
        }
        finally { vm.Dispose(); }
    }
}
