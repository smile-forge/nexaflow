using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nexaflow.Features.Common;
using Nexaflow.Features.Dicom;
using Nexaflow.Features.Dicom.FileActions;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Features.Dicom;

/// <summary>
/// The two ways the viewer is reached: "As DICOM" on files, and the same on a folder.
/// <para>
/// A study is almost never one file. It arrives as a burned CD — a DICOMDIR index beside a tree of
/// extensionless instances — or as a folder of loose <c>.dcm</c>. So the folder action has to be offered on
/// a drive root, and a multi-file selection has to reach the tab as one container rather than as the first
/// file with the rest dropped. All that separates these paths is the tab parameters they hand the shell,
/// which is what these assert.
/// </para>
/// </summary>
[TestClass]
[CoversNode("dicom-open-actions")]
public class DicomOpenActionTests
{
    private static (IShellServices Shell, List<Dictionary<string, string>> Opened) Shell()
    {
        var shell = Substitute.For<IShellServices>();
        var opened = new List<Dictionary<string, string>>();
        shell.When(s => s.OpenTab(DicomTabRegistration.StaticPageKind, Arg.Any<Dictionary<string, string>>()))
             .Do(ci => opened.Add(ci.Arg<Dictionary<string, string>>()));
        return (shell, opened);
    }

    // ── "As DICOM" on files ───────────────────────────────────────────────────

    [TestMethod]
    public void AsDicom_OnOneFile_OpensThatFile()
    {
        var (shell, opened) = Shell();

        Assert.IsTrue(new ShowDicomAction(shell).PerformAction(@"C:\study\IM_0001"));

        Assert.AreEqual(@"C:\study\IM_0001", opened.Single()["path"]);
    }

    [TestMethod]
    public void AsDicom_OnASelection_OpensOneTabHoldingAllOfThem()
    {
        var (shell, opened) = Shell();
        string[] files = [@"C:\s\a.dcm", @"C:\s\b.dcm", @"C:\s\c.dcm"];

        Assert.IsTrue(new ShowDicomAction(shell).PerformAction(files));

        Assert.AreEqual(1, opened.Count, "a selection is one study, not three tabs");
        CollectionAssert.AreEqual(files, opened.Single()["paths"].Split('|'),
                                  "every selected instance reaches the container, in order");
    }

    [TestMethod]
    public void AsDicom_OnAnEmptySelection_OpensNothing()
    {
        var (shell, opened) = Shell();

        Assert.IsFalse(new ShowDicomAction(shell).PerformAction([]));

        Assert.AreEqual(0, opened.Count, "an empty tab with nothing to view is worse than no tab");
    }

    [TestMethod]
    public void AsDicom_IsNonDestructive_AndOwnsTheDicomExperience()
    {
        var action = new ShowDicomAction(Substitute.For<IShellServices>());

        Assert.IsFalse(action.IsDestructive, "a viewer never writes");
        Assert.IsTrue(action.OpensViewer);
        Assert.IsTrue(action.SupportsMultipleFiles);
        Assert.AreEqual("/dicom", action.ExperienceId);
    }

    // ── "As DICOM" on a folder ────────────────────────────────────────────────

    [TestMethod]
    public void AsDicom_OnAFolder_HandsTheFolderOver_AndLetsTheViewerFindTheIndex()
    {
        var (shell, opened) = Shell();

        Assert.IsTrue(new DicomFolderAction(shell).PerformAction(@"D:\STUDY01"));

        Assert.AreEqual(@"D:\STUDY01", opened.Single()["path"]);
    }

    [TestMethod]
    public void TheFolderActionIsOfferedOnADriveRoot_BecauseThatIsWhereAStudyCdSits()
    {
        var action = new DicomFolderAction(Substitute.For<IShellServices>());

        Assert.IsTrue(action.AppliesToDrives, "a burned study is opened from the drive, not a subfolder");
        Assert.IsTrue(action.AppliesToRoot);
        CollectionAssert.Contains(action.ContainsFileGlobs, "DICOMDIR",
                                  "the index a CD carries is what makes the folder worth offering");
        CollectionAssert.Contains(action.ContainsFileGlobs, "*.dcm");
    }

    // ── Tab parameters ────────────────────────────────────────────────────────

    [TestMethod]
    public void ADicomDirTabIsTitledForItsStudyFolder_NotTheWordDICOMDIR()
    {
        var dir = Path.Combine(Path.GetTempPath(), "nexa-dicom-title-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "STUDY01"));
        try
        {
            var dicomdir = Path.Combine(dir, "STUDY01", "DICOMDIR");
            File.WriteAllText(dicomdir, "");

            var page = new DicomTabRegistration(Substitute.For<IShellServices>())
                       .CreatePageDefinition(new Dictionary<string, string> { ["path"] = dicomdir });

            Assert.AreEqual("STUDY01", page.Title,
                            "every CD's index file is called DICOMDIR — the folder is the only distinguishing name");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { } }
    }

    [TestMethod]
    public void AMultiFileTabIsTitledByCount()
    {
        var page = new DicomTabRegistration(Substitute.For<IShellServices>())
                   .CreatePageDefinition(new Dictionary<string, string> { ["paths"] = @"a.dcm|b.dcm|c.dcm" });

        Assert.AreEqual("DICOM (3)", page.Title);
    }
}
