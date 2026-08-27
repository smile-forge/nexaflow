using Nexaflow.Features.Common;
using Nexaflow.Tests.Features.FileActions;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Features.Viewers.FileActions;

// The actions these features offer that do NOT open a tab — extract, scan, export, mount. They get the base
// tier only, because the single invocation that is safe to make of them is the one with nothing selected;
// anything else runs an antivirus scan, writes files out, or mounts a disk.
//
// MountDiskAction is why this file exists: it returned true for an empty selection having mounted nothing,
// and mounted every file in a selection while declaring SupportsMultipleFiles false — the same defect found
// in OpenAsArchiveAction and OpenAsDiskAction, in the one action the viewer tier could not reach.

[TestClass]
[CoversNode("vdisk-mount-service")]
public class MountDiskActionConformance : FileActionConformanceTests
{
    protected override IFileAction CreateAction() =>
        new Nexaflow.Features.VirtualDisk.FileActions.MountDiskAction(Substitute.For<IShellServices>());
}

[TestClass]
[CoversNode("compressed-unzip-here")]
public class UnzipHereActionConformance : FileActionConformanceTests
{
    protected override IFileAction CreateAction() =>
        new Nexaflow.Features.Compressed.FileActions.UnzipHereAction(Substitute.For<IShellServices>());
}

[TestClass]
[CoversNode("av-scan-action")]
public class AvScanActionConformance : FileActionConformanceTests
{
    protected override IFileAction CreateAction() =>
        new Nexaflow.Features.Executable.FileActions.AvScanAction(Substitute.For<IShellServices>());
}

[TestClass]
[CoversNode("pdf-extract-images")]
public class ExtractImagesActionConformance : FileActionConformanceTests
{
    protected override IFileAction CreateAction() =>
        new Nexaflow.Features.Pdf.FileActions.ExtractImagesAction(Substitute.For<IShellServices>());
}
