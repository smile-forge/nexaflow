using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.UIJourneys.Infrastructure;
using Point = System.Drawing.Point;

namespace Nexaflow.Tests.Core.UI;

/// <summary>
/// What is dragged in from another application lands wherever the shell says it can: on the page, on the tabs, on the
/// ribbon — and never costs the source its file.
/// </summary>
[TestClass]
[CoversNode("chrome-drop-routing")]
public class DropJourneyTests : UiJourneyTestBase
{
    protected override string? LaunchTabKind => "Scratchpad";

    [DllImport("user32.dll")]
    private static extern bool MoveWindow(IntPtr window, int x, int y, int width, int height, bool repaint);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    private string _folder = null!;

    protected override void OnUISetup()
    {
        _folder = TestTemp.CreateSubdirectory("drops-").FullName;
    }

    /// <summary>The four ways something arrives from another application, one scenario each. Explorer goes last: it rearranges the windows.</summary>
    [TestMethod]
    [CoversNode("universal-drop-target")]
    [CoversNode("note-attachments")]
    [CoversNode("chrome-tabs-drop-open")]
    public void Drop_Journey()
    {
        RunStep("what is dragged in lands on the scratchpad, the ribbon and the tabs", WhatIsDraggedInFromAnotherApplicationLands);
        RunStep("what is dropped on a note is attached to it", WhatIsDroppedOnANoteIsAttachedToIt);
        RunStep("what is dropped on the shell opens and is never run", WhatIsDroppedOnTheShellOpensAndIsNeverRun);
        RunStep("a file dragged from Explorer lands and stays where it was", AFileDraggedFromExplorerLandsAndStaysWhereItWas);
        AssertJourney();
    }

    private void WhatIsDraggedInFromAnotherApplicationLands()
    {
        var canvas = WaitForId("Scratchpad_Canvas", 15);
        Assert.IsNotNull(canvas, "The scratchpad did not open.");
        var from = new Point(canvas.BoundingRectangle.Left + 70, canvas.BoundingRectangle.Bottom - 50);

        var onBoard = Sample("board.txt");
        var notes = Notes();
        var answer = OutsideDrag.Files(from, Within(canvas, 0.7, 0.6), onBoard);
        Check("A file dropped on the scratchpad becomes a note", () => WaitForFs(() => Notes() == notes + 1, 4));
        Check($"The board never tells the source to delete its file (said {answer})", () => !answer.HasFlag(DragDropEffects.Move));
        Check("The board leaves the file where it was", () => File.Exists(onBoard));

        notes = Notes();
        answer = OutsideDrag.Words(from, Within(canvas, 0.45, 0.75), "Words from another application");
        Check("Text dropped on the scratchpad becomes a note", () => WaitForFs(() => Notes() == notes + 1, 4));
        Check($"The board never tells the source to delete its words (said {answer})", () => !answer.HasFlag(DragDropEffects.Move));

        if (WaitForId("RibbonBarControl") is { } ribbon)
        {
            var pinned = Sample("pinned.txt");
            var buttons = RibbonButtons(ribbon);
            answer = OutsideDrag.Files(from, Within(ribbon, 0.75, 0.5), pinned);
            Check("A file dropped on the ribbon becomes a shortcut", () => WaitForFs(() => RibbonButtons(ribbon) > buttons, 4));
            Check($"The ribbon never tells the source to delete its file (said {answer})", () => !answer.HasFlag(DragDropEffects.Move));
            Check("The ribbon leaves the file where it was", () => File.Exists(pinned));
        }
        else Check("The ribbon is there", () => false);

        if (WaitForId("TabStrip") is { } tabs)
        {
            var opened = Sample("opened.txt");
            var count = Tabs();
            answer = OutsideDrag.Files(from, Within(tabs, 0.6, 0.5), opened);
            Check("A file dropped on the tabs opens in a tab", () => WaitForFs(() => Tabs() == count + 1, 8));
            Check($"The tabs never tell the source to delete its file (said {answer})", () => !answer.HasFlag(DragDropEffects.Move));
            Check("The tabs leave the file where it was", () => File.Exists(opened));
        }
        else Check("The tab strip is there", () => false);
    }

    /// <summary>A note takes what is dropped on it, and a file that is only its content is kept.</summary>
    private void WhatIsDroppedOnANoteIsAttachedToIt()
    {
        var canvas = WaitForId("Scratchpad_Canvas", 15);
        Assert.IsNotNull(canvas, "The scratchpad did not open.");
        var from = new Point(canvas.BoundingRectangle.Left + 70, canvas.BoundingRectangle.Bottom - 50);
        var at = Within(canvas, 0.6, 0.55);

        var notes = Notes();
        OutsideDrag.Words(from, at, "A note to attach to");
        Check("The note is made", () => WaitForFs(() => Notes() == notes + 1, 4));

        notes = Notes();
        var attached = Sample("attached-by-path.txt");
        OutsideDrag.Files(from, at, attached);
        Check("A file dropped on the note is in it", () => WaitForFs(() => BoardSays("attached-by-path"), 8));
        Check("and no other note is made for it", () => Notes() == notes);

        OutsideDrag.Attachment(from, at, "from-a-mail.txt", "the attachment"u8.ToArray());
        Check("A file that is only content is kept with the note", () => WaitForFs(
            () => Directory.EnumerateFiles(ConfigDir, "from-a-mail.txt", SearchOption.AllDirectories).Any(), 8));

        OutsideDrag.Words(from, at, "and more words");
        Check("Words dropped on the note are written in it", () => WaitForFs(() => BoardSays("and more words"), 8));
        Check("still with no other note made", () => Notes() == notes);
    }

    /// <summary>A shortcut opens what was dropped, and a script dropped on the tabs is read rather than run.</summary>
    private void WhatIsDroppedOnTheShellOpensAndIsNeverRun()
    {
        var canvas = WaitForId("Scratchpad_Canvas", 15);
        Assert.IsNotNull(canvas, "The scratchpad did not open.");
        var from = new Point(canvas.BoundingRectangle.Left + 70, canvas.BoundingRectangle.Bottom - 50);

        var ribbon = WaitForId("RibbonBarControl");
        var strip = WaitForId("TabStrip");
        Assert.IsNotNull(ribbon, "There is no ribbon.");
        Assert.IsNotNull(strip, "There is no tab strip.");

        var script = Path.Combine(_folder, "never-run.cmd");
        var ran = Path.Combine(_folder, "it-ran");
        File.WriteAllText(script, $"echo ran> \"{ran}\"\r\n");
        var count = Tabs();
        OutsideDrag.Files(from, Within(strip, 0.6, 0.5), script);

        Check("A script dropped on the tabs opens in a viewer", () => WaitForFs(() => Tabs() == count + 1, 8));
        Check("and is not run", () => !WaitForFs(() => File.Exists(ran), 3));

        var shortcut = Sample("shortcut-target.txt");
        OutsideDrag.Files(from, Within(ribbon, 0.75, 0.5), shortcut);
        AutomationElement? button = null;
        // The button, not the tooltip of it that the pointer left behind when it let go there.
        Check("The shortcut is named as its file", () => (button = WaitForId("Ribbon_shortcut-target.txt", 5)) is not null);
        if (button is not null)
        {
            count = Tabs();

            Check("The shortcut opens its file", () =>
            {
                var b = button.BoundingRectangle;
                FlaUI.Core.Input.Mouse.Click(new Point(b.Left + b.Width / 2, b.Top + b.Height / 2));
                return WaitForFs(() => Tabs() == count + 1, 8);
            });
        }
    }

    /// <summary>Whether the board's saved notes say <paramref name="words"/>.</summary>
    private bool BoardSays(string words)
    {
        foreach (var file in Directory.EnumerateFiles(ConfigDir, "*.json", SearchOption.AllDirectories))
        {
            try { if (File.ReadAllText(file).Contains(words, StringComparison.Ordinal)) return true; }
            catch (IOException) { /* being written: the next look */ }
        }
        return false;
    }

    /// <summary>The one source that matters most, itself: Explorer deletes what a target says it moved.</summary>
    private void AFileDraggedFromExplorerLandsAndStaysWhereItWas()
    {
        Assert.IsNotNull(WaitForId("Scratchpad_Canvas", 15), "The scratchpad did not open.");
        var file = Sample("from-explorer.txt");

        // Side by side: Explorer on the left third, the app on the rest.
        int width = GetSystemMetrics(0), height = GetSystemMetrics(1) - 80, third = width / 3;
        MoveWindow(MainWindow.Properties.NativeWindowHandle, third, 0, width - third, height, true);

        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_folder}\"") { UseShellExecute = true })?.Dispose();
        var title = Path.GetFileName(_folder);
        var explorer = WaitFor(() => Automation.GetDesktop().FindAllChildren()
            .FirstOrDefault(w => w.Properties.ClassName.ValueOrDefault == "CabinetWClass"
                              && (w.Properties.Name.ValueOrDefault ?? string.Empty).Contains(title, StringComparison.OrdinalIgnoreCase)), 20);
        Assert.IsNotNull(explorer, "Explorer did not open the folder.");

        try
        {
            MoveWindow(explorer.Properties.NativeWindowHandle, 0, 0, third, height, true);
            explorer.AsWindow().SetForeground();

            // By its extension or without: Explorer may hide extensions.
            var item = WaitFor(() => explorer.FindAllDescendants(cf => cf.ByControlType(ControlType.ListItem))
                .FirstOrDefault(i => (i.Properties.Name.ValueOrDefault ?? string.Empty).StartsWith("from-explorer", StringComparison.Ordinal)), 15);
            Assert.IsNotNull(item, "Explorer did not list the file.");

            var canvas = WaitForId("Scratchpad_Canvas")!;
            var notes = Notes();
            var at = item.BoundingRectangle;
            OutsideDrag.Press(new Point(at.Left + 30, at.Top + at.Height / 2), Within(canvas, 0.6, 0.6));

            Check("A file dragged from Explorer becomes a note", () => WaitForFs(() => Notes() == notes + 1, 5));
            Check("Explorer does not delete what was dropped", () => !WaitForFs(() => !File.Exists(file), 5));
        }
        finally
        {
            try { explorer.AsWindow().Close(); } catch (COMException) { /* already closed */ }
        }
    }

    private string Sample(string name)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllText(path, "dropped");
        return path;
    }

    private static Point Within(AutomationElement element, double across, double down)
    {
        var r = element.BoundingRectangle;
        return new Point((int)(r.Left + r.Width * across), (int)(r.Top + r.Height * down));
    }

    private int Notes() => MainWindow.FindAllDescendants(cf => cf.ByAutomationId("Scratchpad_NoteRemove")).Length;

    private static int RibbonButtons(AutomationElement ribbon)
        => ribbon.FindAllDescendants(cf => cf.ByControlType(ControlType.Button)).Length;

    private int Tabs() => MainWindow.FindAllDescendants()
        .Count(e => (e.Properties.AutomationId.ValueOrDefault ?? string.Empty).StartsWith("TabItem_", StringComparison.Ordinal));
}
