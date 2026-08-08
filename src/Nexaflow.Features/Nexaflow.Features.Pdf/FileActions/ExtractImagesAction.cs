using System.Collections.Generic;
using System.Linq;
using Nexaflow.Features.Common;
using Nexaflow.Features.Pdf.Services;

namespace Nexaflow.Features.Pdf.FileActions;

/// <summary>
/// Writes a PDF's embedded images into a folder the user picks. Offered on the action strip via the
/// <c>/document/pdf</c> experience, which <c>default-filemap.json</c> maps to <c>*.pdf</c> as an
/// <c>OptionalExtension</c> — a secondary capability, so it never becomes what double-clicking a PDF does.
/// </summary>
public sealed class ExtractImagesAction(IShellServices shell) : IFileAction, ICacheable
{
    public static string? StaticExperienceId => "/document/pdf";
    public string ExperienceId => "/document/pdf";
    public string ExperienceDescription => "PDF document";

    public string DisplayName => "Extract images";
    public string Icon => "🖼";
    public string? Tooltip => "Save the images embedded in this PDF to a folder";

    public bool IsDestructive => false;
    public bool SupportsMultipleFiles => true;
    public bool CanPerformAction => true;
    public bool OpensViewer => false;

    // The strip's refresh fires as soon as this returns, which is before the user has even picked a folder.
    // The export asks for one itself when the files actually exist.
    public bool RequiresRefresh => false;

    // The picker and the extraction both happen after this returns, so the strip's success tick would be
    // claiming an outcome nobody knows yet — including the very likely "user cancelled the folder picker".
    public bool ShowsSuccessTick => false;

    public bool PerformAction(string filePath) => PerformAction([filePath]);

    public bool PerformAction(IEnumerable<string> filePaths)
    {
        var paths = filePaths.ToList();
        if (paths.Count == 0) return false;

        // Fire-and-forget: picking a folder and reading the documents can't block the UI thread, and the
        // shell's own notifications carry the result back.
        _ = PdfImageExport.RunAsync(shell, paths);
        return true;
    }
}
