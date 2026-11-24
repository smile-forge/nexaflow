using System.IO;
using System.Windows;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.UIJourneys.Infrastructure;
using Point = System.Drawing.Point;

namespace Nexaflow.Tests.Features.WindowsFileSystem.UI;

/// <summary>Files dragged into the file browser from another application.</summary>
[TestClass]
[CoversNode("winfs-drag-drop")]
public class FileBrowserDropJourneyTests : UiJourneyTestBase
{
    private string _into = null!;

    protected override string? LaunchTabKind => "FileSystem";

    protected override IReadOnlyDictionary<string, string> LaunchTabParams
        => new Dictionary<string, string> { ["mode"] = "path", ["path"] = _into, ["label"] = "Drops" };

    protected override void SeedConfig(string configDir) => _into = TestTemp.CreateSubdirectory("drops-into-").FullName;

    [TestMethod]
    [SecondLaunch(SecondLaunchReason.OverTwoMinutes, "The file-system journey takes about 110 s on its own, so the drops would carry it past two minutes; this one also starts in a folder of its own.")]
    [CoversNode("winfs-right-drag-menu")]
    public void FilesDraggedIntoTheBrowser_AreCopiedOrMoved_InOnePass()
    {
        var list = WaitForId("FileListView", 15);
        Assert.IsNotNull(list, "The file list did not open.");
        var r = list.BoundingRectangle;
        var from = new Point(r.Left + 70, r.Bottom - 50);
        var background = new Point(r.Left + r.Width / 2, r.Top + r.Height * 3 / 5);
        var away = TestTemp.CreateSubdirectory("drops-from-").FullName;

        var copied = Path.Combine(away, "copied.txt");
        File.WriteAllText(copied, "copy me");
        var answer = OutsideDrag.Files(from, background, copied);
        Check("A file dropped in is copied", () => WaitForFs(() => File.Exists(Path.Combine(_into, "copied.txt")), 8));
        Check($"and the source told it was copied (said {answer})", () => answer == DragDropEffects.Copy && File.Exists(copied));

        var moved = Path.Combine(away, "moved.txt");
        File.WriteAllText(moved, "move me");
        Keyboard.Press(VirtualKeyShort.SHIFT);
        try { answer = OutsideDrag.Files(from, background, moved); }
        finally { Keyboard.Release(VirtualKeyShort.SHIFT); }
        Check("With Shift it is moved", () => WaitForFs(() => File.Exists(Path.Combine(_into, "moved.txt")) && !File.Exists(moved), 8));
        Check($"by us, the source not being told to delete it (said {answer})", () => !answer.HasFlag(DragDropEffects.Move));

        OutsideDrag.Attachment(from, background, "attachment.txt", "attached"u8.ToArray());
        Check("A file that is only content is saved", () => WaitForFs(() => File.Exists(Path.Combine(_into, "attachment.txt")), 8));

        var chosen = Path.Combine(away, "chosen.txt");
        File.WriteAllText(chosen, "ask me");
        answer = OutsideDrag.FilesByTheRightButton(from, background, chosen);
        var moveHere = MenuItemNamed("Move here");
        Check("The right button asks what is meant", () => moveHere is not null);
        Check("and does nothing until it is answered", () => File.Exists(chosen) && !File.Exists(Path.Combine(_into, "chosen.txt")));
        Check($"the source not being told to delete it (said {answer})", () => !answer.HasFlag(DragDropEffects.Move));
        moveHere?.Click();
        Check("Move here moves it", () => WaitForFs(() => File.Exists(Path.Combine(_into, "chosen.txt")) && !File.Exists(chosen), 8));

        AssertJourney();
    }
}
