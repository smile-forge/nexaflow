using System;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Common.Theming;

namespace Nexaflow.Tests.Visuals.Controls;

/// <summary>
/// The zoom every text surface shares, and the shell setting it sits on top of. Plain objects — no WPF —
/// so these run without an STA thread; the chip that draws them is covered by <see cref="ZoomChipTests"/>.
/// </summary>
[TestClass]
// The shell text size is process-wide state, so these cannot run beside each other: one test's
// assignment is another's assertion. Serialised rather than faked, because the weak-listener behaviour
// being pinned here only exists on the real static.
[DoNotParallelize]
[CoversNode("vcommon-text-zoom")]
public class TextZoomTests
{
    // The setting is process-wide, so a test that moves it has to put it back or it leaks into the next one.
    [TestCleanup]
    public void Cleanup() => TextTypography.BaseFontSize = TextTypography.DefaultBaseFontSize;

    [TestMethod]
    [TestCategory("Unit")]
    public void Unit_Zoom_StepsAndClampsWithinBounds()
    {
        var zoom = new TextZoom();
        Assert.AreEqual(100, zoom.Percent);
        CollectionAssert.Contains(zoom.Presets.ToArray(), 130, "130% is an offered preset");

        zoom.ZoomInCommand.Execute(null);
        Assert.AreEqual(100 + TextZoom.Step, zoom.Percent);

        zoom.ResetZoomCommand.Execute(null);
        Assert.AreEqual(100, zoom.Percent);

        zoom.Percent = 10_000;
        Assert.AreEqual(TextZoom.MaxPercent, zoom.Percent, "clamps to the ceiling");

        zoom.Percent = 1;
        Assert.AreEqual(TextZoom.MinPercent, zoom.Percent, "clamps to the floor");

        for (var i = 0; i < 100; i++) zoom.ZoomOutCommand.Execute(null);
        Assert.AreEqual(TextZoom.MinPercent, zoom.Percent, "Zoom Out never drops below the floor");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Unit_FontSize_IsTheShellSizeAtThisZoom()
    {
        TextTypography.BaseFontSize = 20;
        var zoom = new TextZoom();

        Assert.AreEqual(20d, zoom.FontSize, 1e-9, "100% is the shell size unchanged");

        zoom.Percent = 150;
        Assert.AreEqual(30d, zoom.FontSize, 1e-9);

        zoom.Percent = 50;
        Assert.AreEqual(10d, zoom.FontSize, 1e-9);
    }

    /// <summary>
    /// The whole point of routing both inputs through one number: a viewer binds <c>FontSize</c> and gets
    /// a live Options change for free, without knowing the setting exists.
    /// </summary>
    [TestMethod]
    [TestCategory("Unit")]
    public void Unit_ChangingTheShellSize_RaisesFontSizeOnALiveZoom()
    {
        var zoom = new TextZoom { Percent = 200 };
        var raised = 0;
        zoom.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(TextZoom.FontSize)) raised++; };

        TextTypography.BaseFontSize = 16;

        Assert.AreEqual(1, raised, "a shell size change must reach an open tab's zoom");
        Assert.AreEqual(32d, zoom.FontSize, 1e-9);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Unit_ShellSize_IsClampedAndSurvivesRubbish()
    {
        TextTypography.BaseFontSize = 10_000;
        Assert.AreEqual(TextTypography.MaxBaseFontSize, TextTypography.BaseFontSize);

        TextTypography.BaseFontSize = 1;
        Assert.AreEqual(TextTypography.MinBaseFontSize, TextTypography.BaseFontSize);

        // A config field that was never written deserializes as 0, and NaN can arrive from a bad edit.
        // Either would render the app unreadable, so both fall back rather than being clamped to the floor.
        Assert.AreEqual(TextTypography.DefaultBaseFontSize, TextTypography.Clamp(0));
        Assert.AreEqual(TextTypography.DefaultBaseFontSize, TextTypography.Clamp(double.NaN));
    }

    /// <summary>
    /// Nothing disposes a <see cref="TextZoom"/> — a tab just closes. The registration on the process-wide
    /// setting is weak precisely so that is safe, and this is what proves it: a collected zoom must not be
    /// kept alive by the setting it listens to.
    /// </summary>
    [TestMethod]
    [TestCategory("Unit")]
    public void Unit_AClosedTabsZoom_IsNotKeptAliveByTheShellSetting()
    {
        var weak = CreateAndAbandon();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.IsFalse(weak.TryGetTarget(out _), "TextTypography must hold its listeners weakly");

        // …and the pruned registration does not break the next notification.
        TextTypography.BaseFontSize = 18;
        Assert.AreEqual(18d, TextTypography.BaseFontSize);
    }

    // Separate method so the local goes out of scope before the collection — a local still in the frame
    // can stay rooted in a Debug build.
    private static WeakReference<TextZoom> CreateAndAbandon() => new(new TextZoom { Percent = 120 });
}
