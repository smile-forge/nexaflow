using System.IO;
using Nexaflow.IO.Common;
using Nexaflow.IO.Common.Locations;
using Nexaflow.Visuals.Terminal.Models;

namespace Nexaflow.Visuals.Terminal;

/// <summary>
/// What the terminal's Files panel lists for a folder: files first, then folders, each alphabetical, read through the file
/// system the shell's folder is on — a WSL distro's as well as this disk's.
/// <para>
/// The order is the point and it is deliberately not Explorer's. The panel exists mainly to drag a path
/// onto the console or the AI bar, and it is a file you nearly always want — putting the folders first
/// would bury them. Kept out of the view-model so the rule is one testable call rather than a loop wrapped
/// around a live shell's current directory.
/// </para>
/// </summary>
public static class TerminalFileList
{
    /// <summary>Lists <paramref name="folder"/>, each entry's path shown as <paramref name="show"/> names it. A folder that
    /// cannot be read — missing, denied, its location gone — lists what was read of it rather than throwing: the panel sits
    /// beside a live shell that can be anywhere, including somewhere it has just been denied.</summary>
    public static async Task<IReadOnlyList<TerminalFsEntry>> ListAsync(IVirtualFileSystem files, string? folder,
                                                                      Func<string, string> show, CancellationToken ct)
    {
        var read = new List<LocationEntry>();
        if (string.IsNullOrEmpty(folder)) return [];
        try
        {
            await foreach (var entry in files.EnumerateAsync(folder, ct).ConfigureAwait(false)) read.Add(entry);
        }
        catch (Exception unreadable) when (unreadable is LocationException or IOException or UnauthorizedAccessException
                                                         or NotSupportedException or OperationCanceledException)
        {
            // what was read of it stands
        }

        return [.. read.Where(e => !e.IsDirectory).OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).Select(Entry),
                .. read.Where(e => e.IsDirectory).OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).Select(Entry)];

        // The file system lists a name as the segment its path holds; its display name is the name as its folder has it.
        TerminalFsEntry Entry(LocationEntry entry)
        {
            var path = Path.Combine(folder, entry.Name);
            return new TerminalFsEntry(path, files.GetDisplayName(path), show(path), entry.IsDirectory);
        }
    }
}
