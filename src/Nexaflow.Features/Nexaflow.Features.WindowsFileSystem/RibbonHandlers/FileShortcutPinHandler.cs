using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.Ribbon;
using Nexaflow.Icons;
using Nexaflow.Interaction;
using Nexaflow.IO.Common;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.WindowsFileSystem.RibbonHandlers;

/// <summary>
/// A file or folder dropped on the ribbon, kept there as a shortcut: clicked, it opens as a double-click on it would.
/// Only what has a path of its own is taken — a file that came as content would be a shortcut to a temporary copy.
/// </summary>
public sealed class FileShortcutPinHandler(IShellServices shell, IVirtualFileSystem vfs) : IRibbonPinHandler, IRibbonItemExecutor
{
    internal const string Kind = "FileShortcut";
    internal const string KeyPath = "path";

    public IReadOnlyList<string> AcceptedKinds { get; } = [DragKinds.Files];

    public string PageKind => Kind;

    public IReadOnlyList<RibbonPinResult> Pin(IDragPayload payload) => [.. payload.Files().Select(ShortcutTo)];

    private static RibbonPinResult ShortcutTo(string path)
    {
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var name = Path.GetFileName(trimmed);

        return new RibbonPinResult
        {
            PageKind   = Kind,
            Label      = name.Length > 0 ? name : trimmed,
            // By its name alone: whether a path is a folder is a round trip on a share, and this is inside the drop.
            Icon       = IconRef.Emoji(Path.HasExtension(trimmed) ? "📄" : "📁"),
            PageParams = new() { [KeyPath] = path },
        };
    }

    public void Execute(Dictionary<string, string>? pageParams, IRibbonExecutionContext context)
    {
        if (pageParams?.GetValueOrDefault(KeyPath) is { Length: > 0 } path) _ = OpenAsync(path, context);
    }

    /// <summary>Nothing awaits a ribbon click, so a failure is shown here rather than thrown.</summary>
    private async Task OpenAsync(string path, IRibbonExecutionContext context)
    {
        try
        {
            if (await vfs.ExistsAsync(path))
                shell.HandleObject(path);
            else
                context.ShowConfirmation(
                    Str.Get("WindowsFileSystem.Ribbon.FilesNotFoundTitle"),
                    Str.Get("WindowsFileSystem.Ribbon.FilesNotFoundMessage"),
                    context.RemoveCurrentRibbonItem);
        }
        catch (Exception ex) when (ex is FileOperationException or IOException)
        {
            context.ShowError(ex.Message);
        }
    }
}
