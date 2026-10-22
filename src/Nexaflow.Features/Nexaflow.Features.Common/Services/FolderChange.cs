using System.Collections.Generic;

namespace Nexaflow.Features.Common;

/// <summary>
/// What changed in a folder watched through <see cref="IShellServices.WatchFolder"/> since its watcher was last told: the
/// entries the shell named (full paths, renamed entries under both names), and whether it said the folder itself changed —
/// in which case anything in it may have.
/// </summary>
public sealed record FolderChange(IReadOnlySet<string> Items, bool Whole)
{
    /// <summary>Whether <paramref name="path"/> may have changed.</summary>
    public bool Touches(string path) => Whole || Items.Contains(path);
}
