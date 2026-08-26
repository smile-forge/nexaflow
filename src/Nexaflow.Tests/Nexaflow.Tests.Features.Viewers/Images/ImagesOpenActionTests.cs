using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nexaflow.Features.Common;
using Nexaflow.Features.Images.FileActions;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Features.Images;

/// <summary>
/// The four ways images reach the viewer: the "As Image" file action for a file or a multi-selection, and
/// the Slideshow / Album folder actions.
/// <para>
/// All that separates them is the tab parameters they hand the shell — the path list, the initial view and
/// the folder scope. Get one wrong and the tab opens on the wrong images, or in the wrong mode, with no
/// error to show for it; these tests assert the payload rather than the tab.
/// </para>
/// </summary>
[TestClass]
public class ImagesOpenActionTests
{
    private static (IShellServices Shell, List<Dictionary<string, string>> Opened) Shell()
    {
        var shell = Substitute.For<IShellServices>();
        var opened = new List<Dictionary<string, string>>();
        shell.When(s => s.OpenTab("Images", Arg.Any<Dictionary<string, string>>()))
             .Do(ci => opened.Add(ci.Arg<Dictionary<string, string>>()));
        return (shell, opened);
    }

    private static string MakeFolder(params string[] names)
    {
        var dir = Path.Combine(Path.GetTempPath(), "neximgopen_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        foreach (var n in names) File.WriteAllText(Path.Combine(dir, n), "x");
        return dir;
    }


    [TestMethod]
    [CoversNode("images-open-as-image")]
    public void AsImage_OnASelection_TakesTheImagesAndDropsTheRest()
    {
        var (shell, opened) = Shell();

        var handled = new ShowImageAction(shell).PerformAction(
            [@"C:\pics\a.png", @"C:\pics\notes.txt", @"C:\pics\b.jpg"]);

        Assert.IsTrue(handled);
        CollectionAssert.AreEqual(new[] { @"C:\pics\a.png", @"C:\pics\b.jpg" },
                                  opened.Single()["paths"].Split('|'),
                                  "a mixed selection opens the images and silently ignores the rest");
    }

    [TestMethod]
    [CoversNode("images-open-as-image")]
    public void AsImage_OnASelectionWithNoImages_DeclinesRatherThanOpeningAnEmptyTab()
    {
        var (shell, opened) = Shell();

        Assert.IsFalse(new ShowImageAction(shell).PerformAction([@"C:\docs\a.txt", @"C:\docs\b.pdf"]));
        Assert.AreEqual(0, opened.Count);
    }

    // ── Folder actions ────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("images-open-slideshow")]
    public void Slideshow_OpensTheWholeFolderInTheCarousel()
    {
        var dir = MakeFolder("a.png", "b.jpg", "readme.txt");
        try
        {
            var (shell, opened) = Shell();

            Assert.IsTrue(new SlideshowFolderAction(shell).PerformAction(dir));

            var p = opened.Single();
            Assert.AreEqual("slideshow", p["view"]);
            Assert.AreEqual("folder", p["scope"], "the breadcrumb reads as the whole folder, not a selection");
            Assert.AreEqual(2, p["paths"].Split('|').Length, "non-images are left out of the queue");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [TestMethod]
    [CoversNode("images-open-album")]
    public void Album_OpensTheSameFolderInTheGrid()
    {
        var dir = MakeFolder("a.png", "b.jpg");
        try
        {
            var (shell, opened) = Shell();

            Assert.IsTrue(new AlbumFolderAction(shell).PerformAction(dir));

            Assert.AreEqual("album", opened.Single()["view"],
                            "the two folder actions differ only in the view they request");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [TestMethod]
    [CoversNode("images-open-actions")]
    public void AFolderWithNoImages_IsDeclined()
    {
        var dir = MakeFolder("notes.txt", "data.csv");
        try
        {
            var (shell, opened) = Shell();

            Assert.IsFalse(new AlbumFolderAction(shell).PerformAction(dir));
            Assert.AreEqual(0, opened.Count);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [TestMethod]
    [CoversNode("images-open-actions")]
    public void FolderActions_AreOnlyOffered_WhenAFolderIsMostlyImages()
    {
        var (shell, _) = Shell();
        var action = new AlbumFolderAction(shell);

        Assert.AreEqual(30, action.MinimumFileGlobMatchPercentage,
                        "a folder with a stray screenshot in it should not sprout an Album button");
        CollectionAssert.AreEqual(Nexaflow.Features.Images.ImageFileTypes.Globs, action.ContainsFileGlobs);
        Assert.IsTrue(action.AppliesToRoot, "the open folder itself can be viewed as an album");
        Assert.IsFalse(action.IsDestructive);
    }
}
