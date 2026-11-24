using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.UIJourneys.Infrastructure;
using Point = System.Drawing.Point;

namespace Nexaflow.Tests.Features.Images.UI;

/// <summary>
/// What the image editor's journeys do with their hands: click, drag and fit on the edit canvas, move a slider, turn a
/// check box over — in the main window or a popup over it.
/// </summary>
public abstract class ImageEditJourneyBase : UiJourneyTestBase
{
    /// <summary>The middle of the edit canvas — where the picture is fitted.</summary>
    protected Point? CanvasCentre()
    {
        var canvas = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("Image_EditCanvas"));
        if (canvas is null) return null;
        var r = canvas.BoundingRectangle;
        return new Point(r.X + r.Width / 2, r.Y + r.Height / 2);
    }

    /// <summary>Picture editing drives the AI host: a journey that fails keeps the host's log.</summary>
    protected override bool KeepsAppLog => true;

    protected bool ClickCanvas()
    {
        if (CanvasCentre() is not { } centre) return false;
        Mouse.MoveTo(centre);
        Mouse.Click(MouseButton.Left);
        Wait.UntilInputIsProcessed();
        Thread.Sleep(200);
        return true;
    }

    /// <summary>Presses the canvas's own Fit, which centres the photo in it.</summary>
    protected bool FitCanvas()
    {
        var canvas = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("Image_EditCanvas"));
        if (canvas?.FindFirstDescendant(cf => cf.ByName("Fit").And(cf.ByControlType(ControlType.Button)))?.AsButton() is not { } fit) return false;
        fit.Invoke();
        Wait.UntilInputIsProcessed();
        Thread.Sleep(200);
        return true;
    }

    protected bool DragOnCanvas(int fromX, int fromY, int toX, int toY)
    {
        if (CanvasCentre() is not { } centre) return false;
        Mouse.Drag(new Point(centre.X + fromX, centre.Y + fromY), new Point(centre.X + toX, centre.Y + toY), MouseButton.Left);
        Wait.UntilInputIsProcessed();
        Thread.Sleep(200);
        return true;
    }

    /// <summary>
    /// Drags a rectangle, five wide to three high, centred on the canvas and reaching <paramref name="fraction"/> of its shorter
    /// side either way. A number of pixels is a different amount of the picture on every screen: the sample photo is thirty-two
    /// pixels across, fitted to a canvas a thousand pixels tall, and a marquee of forty screen pixels is three of its own.
    /// </summary>
    protected bool DragOnCanvasAcross(double fraction)
    {
        var canvas = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("Image_EditCanvas"));
        if (canvas is null) return false;
        var reach = (int)(Math.Min(canvas.BoundingRectangle.Width, canvas.BoundingRectangle.Height) * fraction);
        return DragOnCanvas(-reach, -reach * 3 / 5, reach, reach * 3 / 5);
    }

    /// <summary>Traces through <paramref name="points"/>, offsets from the canvas's centre, with the left button held — an
    /// outline drawn as a person draws one.</summary>
    protected bool TraceOnCanvas(params (int X, int Y)[] points)
    {
        if (CanvasCentre() is not { } centre || points.Length == 0) return false;
        Mouse.MoveTo(new Point(centre.X + points[0].X, centre.Y + points[0].Y));
        Mouse.Down(MouseButton.Left);
        foreach (var (x, y) in points.Skip(1))
        {
            Mouse.MoveTo(new Point(centre.X + x, centre.Y + y));
            Wait.UntilInputIsProcessed();
        }
        Mouse.Up(MouseButton.Left);
        Wait.UntilInputIsProcessed();
        Thread.Sleep(200);
        return true;
    }

    /// <summary>Moves a slider to whichever end it is not at — in the main window or a popup over it.</summary>
    protected bool MoveSlider(string automationId)
    {
        var element = FindInAppWindows(automationId);
        try { element?.Patterns.ScrollItem.PatternOrDefault?.ScrollIntoView(); } catch { /* not in a scroller */ }
        if (element?.Patterns.RangeValue.PatternOrDefault is not { } range) return false;
        var target = range.Value.Value >= range.Maximum.Value ? range.Minimum.Value : range.Maximum.Value;
        range.SetValue(target);
        return WaitForFs(() => Math.Abs(range.Value.Value - target) < 0.01, 2);
    }

    /// <summary>The slider <paramref name="automationId"/> set to <paramref name="value"/> — for one whose ends are held by another,
    /// as a span's are, where its far end will not do.</summary>
    protected bool MoveSliderTo(string automationId, double value)
    {
        var element = FindInAppWindows(automationId);
        try { element?.Patterns.ScrollItem.PatternOrDefault?.ScrollIntoView(); } catch { /* not in a scroller */ }
        if (element?.Patterns.RangeValue.PatternOrDefault is not { } range) return false;
        range.SetValue(value);
        return WaitForFs(() => Math.Abs(range.Value.Value - value) < 0.01, 2);
    }

    /// <summary>Turns a check box over and back — in the main window or a popup over it.</summary>
    protected bool ToggleAndBack(string automationId)
    {
        if (FindInAppWindows(automationId)?.Patterns.Toggle.PatternOrDefault is not { } toggle) return false;
        var before = toggle.ToggleState.Value;
        toggle.Toggle();
        if (!WaitForFs(() => toggle.ToggleState.Value != before, 2)) return false;
        toggle.Toggle();
        return WaitForFs(() => toggle.ToggleState.Value == before, 2);
    }

    /// <summary>Whether a control takes input — in the main window or a popup over it; one not there is not.</summary>
    protected bool Enabled(string automationId) => FindInAppWindows(automationId)?.IsEnabled == true;

    /// <summary>Opens the panel group <paramref name="automationId"/>, if it is folded — Adjust's Light, Colour and Depth begin
    /// folded — so its controls are there to be driven.</summary>
    protected bool OpenGroup(string automationId)
    {
        if (FindInAppWindows(automationId)?.Patterns.ExpandCollapse.PatternOrDefault is not { } fold) return false;
        if (fold.ExpandCollapseState.Value != FlaUI.Core.Definitions.ExpandCollapseState.Expanded) fold.Expand();
        return WaitForFs(() => fold.ExpandCollapseState.Value == FlaUI.Core.Definitions.ExpandCollapseState.Expanded, 2);
    }

    /// <summary>
    /// A new Artist tab from the ribbon, which shows the start screen while it holds no picture — the app opens into the draft's tab,
    /// so a scenario that begins on the start screen opens its own.
    /// </summary>
    protected void OpenArtistStartScreen()
    {
        if (WaitForId("Ribbon_Artist", 10) is not { } artist) return;
        if (artist.Patterns.Invoke.PatternOrDefault is { } invoke) invoke.Invoke();
        else artist.Click();
        Wait.UntilInputIsProcessed();
    }
}
