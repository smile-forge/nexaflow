using Nexaflow.Features.Common;
using Nexaflow.Tests.Features.FileActions;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Viewers.FileActions;

// Every viewer-opening IFileAction in this suite's features, held to the same contract. Each class is a
// declaration, not a test: the behaviour lives in ViewerActionConformanceTests, so a new viewer action
// costs six lines here and inherits the eight rules — rather than the four-assertions-rewritten-by-hand
// that this replaced, which covered nine of these actions and missed the rest.
//
// Nothing here touches disk. Every one of these actions is a pass-through to IShellServices.OpenTab, and
// the ones that filter do it on the extension alone, so the paths are probes rather than fixtures.

[TestClass]
[CoversNode("audio-open")]
public class ShowAudioActionConformance : ViewerActionConformanceTests
{
    protected override IFileAction CreateAction(IShellServices shell) =>
        new Nexaflow.Features.Audio.FileActions.ShowAudioAction(shell);
    protected override string ExpectedPageKind => "Audio";
    protected override string AcceptableFile   => @"C:\music\track.mp3";
}

[TestClass]
[CoversNode("code-open-action")]
public class ShowCodeActionConformance : ViewerActionConformanceTests
{
    protected override IFileAction CreateAction(IShellServices shell) =>
        new Nexaflow.Features.Code.FileActions.ShowCodeAction(shell);
    protected override string ExpectedPageKind => "Code";
    protected override string AcceptableFile   => @"C:\src\Program.cs";
}

[TestClass]
[CoversNode("compressed-open-as-archive")]
public class OpenAsArchiveActionConformance : ViewerActionConformanceTests
{
    protected override IFileAction CreateAction(IShellServices shell) =>
        new Nexaflow.Features.Compressed.FileActions.OpenAsArchiveAction(shell);
    protected override string ExpectedPageKind => Nexaflow.Features.Compressed.CompressedTabRegistration.StaticPageKind;
    protected override string AcceptableFile   => @"C:\dl\bundle.zip";
}

[TestClass]
[CoversNode("dicom-open-actions")]
public class ShowDicomActionConformance : ViewerActionConformanceTests
{
    protected override IFileAction CreateAction(IShellServices shell) =>
        new Nexaflow.Features.Dicom.FileActions.ShowDicomAction(shell);
    protected override string ExpectedPageKind => Nexaflow.Features.Dicom.DicomTabRegistration.StaticPageKind;
    protected override string AcceptableFile   => @"C:\study\image.dcm";
}

[TestClass]
[CoversNode("email-open")]
public class OpenAsEmailActionConformance : ViewerActionConformanceTests
{
    protected override IFileAction CreateAction(IShellServices shell) =>
        new Nexaflow.Features.Email.FileActions.OpenAsEmailAction(shell);
    protected override string ExpectedPageKind => Nexaflow.Features.Email.EmailTabRegistration.StaticPageKind;
    protected override string AcceptableFile   => @"C:\mail\message.eml";
}

[TestClass]
[CoversNode("open-actions-4")]
public class InspectPeActionConformance : ViewerActionConformanceTests
{
    protected override IFileAction CreateAction(IShellServices shell) =>
        new Nexaflow.Features.Executable.FileActions.InspectPeAction(shell);
    protected override string ExpectedPageKind => Nexaflow.Features.Executable.ExecutableTabRegistration.StaticPageKind;
    protected override string AcceptableFile   => @"C:\bin\app.exe";
}

[TestClass]
[CoversNode("font-open")]
public class OpenAsFontActionConformance : ViewerActionConformanceTests
{
    protected override IFileAction CreateAction(IShellServices shell) =>
        new Nexaflow.Features.Font.FileActions.OpenAsFontAction(shell);
    protected override string ExpectedPageKind => Nexaflow.Features.Font.FontTabRegistration.StaticPageKind;
    protected override string AcceptableFile   => @"C:\fonts\Inter.ttf";
}

[TestClass]
[CoversNode("hex-open-actions")]
public class ShowBinaryActionConformance : ViewerActionConformanceTests
{
    protected override IFileAction CreateAction(IShellServices shell) =>
        new Nexaflow.Features.Hex.FileActions.ShowBinaryAction(shell);
    protected override string ExpectedPageKind => "Hex";
    protected override string AcceptableFile   => @"C:\fw\image.bin";

    // /binary is the file map's catch-all — a file nothing else claims is exactly what this opens, so there
    // is no such thing as a file it does not handle. The probe extension is one more thing it accepts.
    protected override string FileThisActionDoesNotHandle => @"C:\probe\no-extension-at-all";
}

[TestClass]
[CoversNode("images-open-as-image")]
public class ShowImageActionConformance : ViewerActionConformanceTests
{
    protected override IFileAction CreateAction(IShellServices shell) =>
        new Nexaflow.Features.Images.FileActions.ShowImageAction(shell);
    protected override string ExpectedPageKind => "Images";
    protected override string AcceptableFile   => @"C:\pics\photo.png";
}

[TestClass]
[CoversNode("json-open-actions")]
public class ShowJsonActionConformance : ViewerActionConformanceTests
{
    protected override IFileAction CreateAction(IShellServices shell) =>
        new Nexaflow.Features.Json.FileActions.ShowJsonAction(shell);
    protected override string ExpectedPageKind => "Json";
    protected override string AcceptableFile   => @"C:\data\payload.json";
}

[TestClass]
[CoversNode("open-actions")]
public class ShowLogActionConformance : ViewerActionConformanceTests
{
    protected override IFileAction CreateAction(IShellServices shell) =>
        new Nexaflow.Features.Logs.FileActions.ShowLogAction(shell);
    protected override string ExpectedPageKind => "Logs";
    protected override string AcceptableFile   => @"C:\logs\service.log";
}

[TestClass]
[CoversNode("markdown-open-action")]
public class ShowMarkdownActionConformance : ViewerActionConformanceTests
{
    protected override IFileAction CreateAction(IShellServices shell) =>
        new Nexaflow.Features.Markdown.FileActions.ShowMarkdownAction(shell);
    protected override string ExpectedPageKind => "Markdown";
    protected override string AcceptableFile   => @"C:\notes\readme.md";
}

[TestClass]
[CoversNode("open-actions-3")]
public class ShowModel3DActionConformance : ViewerActionConformanceTests
{
    protected override IFileAction CreateAction(IShellServices shell) =>
        new Nexaflow.Features.Model3D.FileActions.ShowModel3DAction(shell);
    protected override string ExpectedPageKind => "Model3D";
    protected override string AcceptableFile   => @"C:\models\part.stl";
}

[TestClass]
[CoversNode("notebook-open")]
public class ShowNotebookActionConformance : ViewerActionConformanceTests
{
    protected override IFileAction CreateAction(IShellServices shell) =>
        new Nexaflow.Features.Notebook.FileActions.ShowNotebookAction(shell);
    protected override string ExpectedPageKind => "Notebook";
    protected override string AcceptableFile   => @"C:\work\analysis.ipynb";
}

[TestClass]
[CoversNode("pdf-open-action")]
public class ShowPdfActionConformance : ViewerActionConformanceTests
{
    protected override IFileAction CreateAction(IShellServices shell) =>
        new Nexaflow.Features.Pdf.FileActions.ShowPdfAction(shell);
    protected override string ExpectedPageKind => Nexaflow.Features.Pdf.PdfTabRegistration.StaticPageKind;
    protected override string AcceptableFile   => @"C:\docs\manual.pdf";
}

[TestClass]
[CoversNode("svg-open-actions")]
public class ShowSvgActionConformance : ViewerActionConformanceTests
{
    protected override IFileAction CreateAction(IShellServices shell) =>
        new Nexaflow.Features.Svg.FileActions.ShowSvgAction(shell);
    protected override string ExpectedPageKind => Nexaflow.Features.Svg.SvgTabRegistration.StaticPageKind;
    protected override string AcceptableFile   => @"C:\art\logo.svg";
}

[TestClass]
[CoversNode("tabular-open-action")]
public class ShowTabularActionConformance : ViewerActionConformanceTests
{
    protected override IFileAction CreateAction(IShellServices shell) =>
        new Nexaflow.Features.Tabular.FileActions.ShowTabularAction(shell);
    protected override string ExpectedPageKind => "Tabular";
    protected override string AcceptableFile   => @"C:\data\people.csv";
}

[TestClass]
[CoversNode("open-actions-2")]
public class ShowTextActionConformance : ViewerActionConformanceTests
{
    protected override IFileAction CreateAction(IShellServices shell) =>
        new Nexaflow.Features.Text.FileActions.ShowTextAction(shell);
    protected override string ExpectedPageKind => "Text";
    protected override string AcceptableFile   => @"C:\notes\scratch.txt";
}

[TestClass]
[CoversNode("video-open")]
public class ShowVideoActionConformance : ViewerActionConformanceTests
{
    protected override IFileAction CreateAction(IShellServices shell) =>
        new Nexaflow.Features.Video.FileActions.ShowVideoAction(shell);
    protected override string ExpectedPageKind => "Video";
    protected override string AcceptableFile   => @"C:\media\clip.mp4";
}

[TestClass]
[CoversNode("vdisk-open-actions")]
public class OpenAsDiskActionConformance : ViewerActionConformanceTests
{
    protected override IFileAction CreateAction(IShellServices shell) =>
        new Nexaflow.Features.VirtualDisk.FileActions.OpenAsDiskAction(shell);
    protected override string ExpectedPageKind => Nexaflow.Features.VirtualDisk.VirtualDiskTabRegistration.StaticPageKind;
    protected override string AcceptableFile   => @"C:\vm\disk.vhd";
}
