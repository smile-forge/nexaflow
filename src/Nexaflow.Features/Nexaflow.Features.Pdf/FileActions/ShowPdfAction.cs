using Nexaflow.Features.Common;
using System.Collections.Generic;
using System.Linq;

namespace Nexaflow.Features.Pdf.FileActions;

/// <summary>
/// Opens a PDF in the Nexaflow reader.
/// <para>
/// Its experience id is a level deeper than <see cref="ExtractImagesAction"/>'s <c>/document/pdf</c>, and
/// deliberately so: <c>default-filemap.json</c> maps <c>*.pdf</c> to <c>/document/pdf/read</c> as a real
/// <c>Extension</c> (the file's primary identity), which propagates up so "Extract images" keeps its place on
/// the action strip — while the extra depth breaks the double-click tie in the reader's favour rather than
/// leaving it to whichever action discovery happened to yield first. Same shape as "As Code" at
/// <c>/text/code</c> beating "As Text" at <c>/text</c>.
/// </para>
/// </summary>
public sealed class ShowPdfAction(IShellServices shell) : IFileAction, ICacheable
{
    public static string? StaticExperienceId => "/document/pdf/read";
    public string ExperienceId => "/document/pdf/read";
    public string ExperienceDescription => "PDF reader";

    public string DisplayName => "As Pdf";
    public string Icon => "📕";
    public string? Tooltip => "Read this PDF in Nexaflow";

    public bool IsDestructive => false;
    public bool SupportsMultipleFiles => false;
    public bool CanPerformAction => true;
    public bool RequiresRefresh => false;

    /// <summary>Opens an internal viewer tab, so the "Define New" wizard lists it as a viewer target.</summary>
    public bool OpensViewer => true;

    public bool PerformAction(string filePath)
    {
        shell.OpenTab(PdfTabRegistration.StaticPageKind, new Dictionary<string, string> { ["path"] = filePath });
        return true;
    }

    public bool PerformAction(IEnumerable<string> filePaths)
        => filePaths.FirstOrDefault() is { } path && PerformAction(path);
}
