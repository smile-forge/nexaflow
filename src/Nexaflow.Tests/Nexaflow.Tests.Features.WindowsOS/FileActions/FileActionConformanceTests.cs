using Nexaflow.Features.Common;
using Nexaflow.Features.WindowsFileSystem.FileActions;
using Nexaflow.Tests.Features.FileActions;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Features.WindowsFileSystem.FileActions;

// The file system's own actions — copy, cut, delete, rename, run, install, properties, open-with, and the
// user-defined external-app ones. None of them opens a tab, so they get the base tier only: the single
// invocation that is safe to make of a delete or a clipboard write is the one with nothing selected.
//
// That is not a formality. Three of these reached their side effect before checking: CopyFiles and CutFiles
// put an empty file-drop list on the clipboard (which replaces whatever the user had copied), and
// FileProperties threw NotImplementedException — reachable, because the ribbon-pin path invokes a pinned
// action directly rather than through the action strip's filtering.

[TestClass]
[CoversNode("winfs-action-strip")]
public class CopyFilesConformance : FileActionConformanceTests
{
    protected override IFileAction CreateAction() => new CopyFiles();
}

[TestClass]
[CoversNode("winfs-act-copy-path")]
public class CopyPathsConformance : FileActionConformanceTests
{
    protected override IFileAction CreateAction() => new CopyPaths();
}

[TestClass]
[CoversNode("winfs-action-strip")]
public class CutFilesConformance : FileActionConformanceTests
{
    protected override IFileAction CreateAction() => new CutFiles();
}

[TestClass]
[CoversNode("winfs-action-strip")]
public class DeleteFileConformance : FileActionConformanceTests
{
    protected override IFileAction CreateAction() => new DeleteFile(Substitute.For<IShellServices>());
}

[TestClass]
[CoversNode("winfs-action-strip")]
public class RenameFileConformance : FileActionConformanceTests
{
    protected override IFileAction CreateAction() => new RenameFile(Substitute.For<IShellServices>());
}

[TestClass]
[CoversNode("winfs-action-strip")]
public class ExecuteFileConformance : FileActionConformanceTests
{
    protected override IFileAction CreateAction() => new ExecuteFile();
}

[TestClass]
[CoversNode("winfs-action-strip")]
public class InstallPackageConformance : FileActionConformanceTests
{
    protected override IFileAction CreateAction() => new InstallPackage();
}

[TestClass]
[CoversNode("winfs-action-strip")]
public class OpenWithActionConformance : FileActionConformanceTests
{
    protected override IFileAction CreateAction() => new OpenWithAction();
}

[TestClass]
[CoversNode("winfs-action-strip")]
public class ShellVerbActionConformance : FileActionConformanceTests
{
    // One action per registered shell verb, so it is constructed with the verb it stands for.
    protected override IFileAction CreateAction() =>
        new ShellVerbAction("open", "Open", "probe.exe \"%1\"", "/");
}

[TestClass]
[CoversNode("winfs-action-strip")]
public class FilePropertiesConformance : FileActionConformanceTests
{
    // Internal, and reachable here through the feature's InternalsVisibleTo. This is the one that threw.
    protected override IFileAction CreateAction() => new FileProperties();
}

[TestClass]
[CoversNode("winfs-action-strip")]
public class CustomActionConformance : FileActionConformanceTests
{
    protected override IFileAction CreateAction() =>
        new CustomAction(new ExternalAppDefinition { DisplayName = "Probe", ApplicationPath = "probe.exe" });
}
