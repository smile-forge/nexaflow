using Nexaflow.IO.Common;
using System;
using System.Collections.Generic;
using System.IO;

namespace Nexaflow.Features.WindowsFileSystem.Operations;

/// <summary>
/// Turns "these sources, that folder" into the exact destination paths a transfer should use, and
/// says which sources are refused outright.
/// <para>
/// It exists so drag-drop and clipboard paste cannot drift apart again. These rules — skip a source
/// that vanished, refuse a folder copied into itself, name a same-folder copy "Copy of x" — lived
/// only in the paste path; drop had none of them, so the same gesture behaved differently depending
/// on which one you used. Both call this now, which makes the agreement structural rather than a
/// matched pair of edits.
/// </para>
/// The <em>(2)</em>, <em>(3)</em> suffixing is deliberately not here: the engine applies
/// <see cref="ConflictPolicy.AutoRename"/> at the moment of the write, so a name that becomes taken
/// while the operation waits in the queue still resolves.
/// </summary>
internal static class FileOperationDestinations
{
    /// <summary>
    /// Plans <paramref name="sources"/> into <paramref name="destinationFolder"/>.
    /// <paramref name="refusals"/> comes back with a sentence per source that will not be attempted.
    /// </summary>
    public static IReadOnlyList<TransferItem> Plan(
        IReadOnlyList<string> sources,
        string destinationFolder,
        bool move,
        out IReadOnlyList<string> refusals)
    {
        var items    = new List<TransferItem>();
        var refused  = new List<string>();
        var destNorm = destinationFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        foreach (var source in sources)
        {
            if (string.IsNullOrWhiteSpace(source)) continue;

            bool isDir  = Directory.Exists(source);
            bool isFile = File.Exists(source);

            // Vanished between the gesture and the drop — nothing to do, and not worth a complaint.
            if (!isDir && !isFile) continue;

            string trimmed = source.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string name    = Path.GetFileName(trimmed);

            if (isDir && IsSelfOrDescendant(destNorm, trimmed))
            {
                refused.Add($"Can't {(move ? "move" : "copy")} \"{name}\" into itself.");
                continue;
            }

            bool sameParent = string.Equals(Path.GetDirectoryName(trimmed), destNorm,
                                            StringComparison.OrdinalIgnoreCase);

            // Landing beside itself needs a different name or it is not a copy of anything. A move
            // to where it already is has nothing to do, so it is dropped rather than renamed.
            if (sameParent)
            {
                if (move) continue;
                items.Add(new TransferItem(source, Path.Combine(destinationFolder, CopyOfName(name, isDir))));
                continue;
            }

            items.Add(new TransferItem(source, Path.Combine(destinationFolder, name)));
        }

        refusals = refused;
        return items;
    }

    /// <summary>True when <paramref name="destination"/> is the folder itself or somewhere inside it.</summary>
    private static bool IsSelfOrDescendant(string destination, string folder)
        => string.Equals(destination, folder, StringComparison.OrdinalIgnoreCase)
        || destination.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    /// <summary>"report.txt" → "Copy of report.txt", matching what paste has always produced.</summary>
    private static string CopyOfName(string name, bool isDirectory)
        => isDirectory
            ? $"Copy of {name}"
            : $"Copy of {Path.GetFileNameWithoutExtension(name)}{Path.GetExtension(name)}";
}
