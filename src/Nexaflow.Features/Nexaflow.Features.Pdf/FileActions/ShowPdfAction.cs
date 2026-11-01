using Nexaflow.Features.Common;
using Nexaflow.Icons;
using Nexaflow.Visuals.Common.Localization;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

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
    public string ExperienceDescription => Str.Get("Pdf.Action.AsPdfDescription");

    public string DisplayName => Str.Get("Pdf.Action.AsPdf");
    public IconRef Icon => IconRef.Emoji("📕");
    public string? Tooltip => Str.Get("Pdf.Action.AsPdfTip");

    public bool IsDestructive => false;
    public bool SupportsMultipleFiles => false;
    public bool CanPerformAction => true;
    public bool RequiresRefresh => false;

    /// <summary>Opens an internal viewer tab, so the "Define New" wizard lists it as a viewer target.</summary>
    public bool OpensViewer => true;

    public Task<bool> PerformActionAsync(string filePath, CancellationToken ct = default)
    {
        shell.OpenTab(PdfTabRegistration.StaticPageKind, new Dictionary<string, string> { ["path"] = filePath });
        return Task.FromResult(true);
    }

    public Task<bool> PerformActionAsync(IEnumerable<string> filePaths, CancellationToken ct = default) => filePaths.FirstOrDefault() is { } path ? PerformActionAsync(path, ct) : Task.FromResult(false);
}
