using System.Collections.Generic;
using System.IO;
using Nexaflow.Features.Common;
using Nexaflow.IO.Common;

namespace Nexaflow.Features.Compressed;

/// <summary>
/// Makes archive files browsable like folders in the file explorer. "Is it a container" is answered by
/// the virtual file system (so it covers exactly the formats with a registered handler); whether
/// expanding is the <i>default</i> double-click action is gated to archive-primary formats — document
/// formats that merely happen to be zip-based (<c>.docx</c>, <c>.xlsx</c>, …) keep their normal open.
/// </summary>
public sealed class ArchiveDynamicFolder : IDynamicFolder
{
    // Zip-based document formats: browsable on request, but not expand-by-default (a double-click should
    // still open the document, not dive into its parts). Packages (.ipa/.nupkg/.vsix/…) are not listed —
    // those behave like ordinary archives and expand by default.
    private static readonly HashSet<string> DocumentExtensions = new(System.StringComparer.OrdinalIgnoreCase)
    {
        ".docx", ".docm", ".xlsx", ".xlsm", ".pptx", ".pptm",
        ".odt", ".ods", ".odp", ".odg", ".ott", ".ots", ".otp",
    };

    public bool CanProcess(string path) => VirtualFileSystem.Instance.IsContainer(path);

    public bool ExpandsByDefault(string path) => !DocumentExtensions.Contains(Path.GetExtension(path));
}
