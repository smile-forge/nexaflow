using System.Collections.Generic;
using Nexaflow.Features.Common;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Dicom;

/// <summary>
/// The origin-breadcrumb mechanism behind "open a DICOM report in its own viewer, but point the trail back
/// to the DICOM page, not the temp file it was extracted to". A registered path makes
/// <see cref="FileBreadcrumbs.SetFileBreadcrumbs"/> build an origin parent crumb instead of a folder crumb.
/// </summary>
[TestClass]
[CoversNode("reports")]
public class OriginBreadcrumbsTests
{
    [TestMethod]
    public void SetOriginBreadcrumb_BuildsOriginParent_ThenLeaf()
    {
        var page = new Page();
        page.SetOriginBreadcrumb("report.pdf", "Dicom",
            new Dictionary<string, string> { ["path"] = @"C:\cd\DICOMDIR" }, "Study CD");

        Assert.AreEqual(2, page.Breadcrumbs.Count);
        Assert.AreEqual("Study CD", page.Breadcrumbs[0].Label);
        Assert.AreEqual("Dicom", page.Breadcrumbs[0].TargetPageKind);
        Assert.AreEqual(@"C:\cd\DICOMDIR", page.Breadcrumbs[0].TargetPageParams!["path"]);
        Assert.AreEqual("report.pdf", page.Breadcrumbs[1].Label);
    }

    [TestMethod]
    public void SetFileBreadcrumbs_ConsultsRegistry_PointsBackToOrigin_NotFolder()
    {
        var temp = @"C:\Temp\nexaflow-dicom\report-abc.pdf";
        OriginBreadcrumbs.Register(temp, "Dicom",
            new Dictionary<string, string> { ["path"] = @"D:\CD\DICOMDIR" }, "Patient CD");
        try
        {
            var page = new Page();
            page.SetFileBreadcrumbs(temp);

            Assert.AreEqual(2, page.Breadcrumbs.Count);
            Assert.AreEqual("Dicom", page.Breadcrumbs[0].TargetPageKind,
                "the parent crumb must target the DICOM page, not a file-system folder");
            Assert.AreEqual("report-abc.pdf", page.Breadcrumbs[1].Label);
        }
        finally
        {
            OriginBreadcrumbs.Clear(temp);
        }
    }

    [TestMethod]
    public void SetFileBreadcrumbs_WithoutRegistration_UsesFolderParent()
    {
        var page = new Page();
        page.SetFileBreadcrumbs(@"C:\Scans\image.png");

        // Parent crumb targets the file-system browser (the normal behaviour), not an origin page.
        Assert.AreEqual(2, page.Breadcrumbs.Count);
        Assert.AreEqual(FileBreadcrumbs.FileSystemPageKind, page.Breadcrumbs[0].TargetPageKind);
    }

    [TestMethod]
    public void Clear_ForgetsRegistration()
    {
        var temp = @"C:\Temp\x.pdf";
        OriginBreadcrumbs.Register(temp, "Dicom", new Dictionary<string, string>(), "X");
        OriginBreadcrumbs.Clear(temp);
        Assert.IsNull(OriginBreadcrumbs.ParentCrumbFor(temp));
    }
}
