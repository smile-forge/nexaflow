using Nexaflow.IO.Location.Wsl.Protocol;

namespace Nexaflow.WslHelper.Files;

/// <summary>Reads files and folders into the entries the wire carries: as <c>lstat</c> sees them, owners and groups by
/// name, and for a link where it points and whether that is a folder.</summary>
internal sealed class EntryReader(AccountNames accounts)
{
    /// <summary>The entry at <paramref name="path"/>, named <paramref name="name"/>, or null with the errno that stopped
    /// it.</summary>
    public WireEntry? Read(string path, string name, out int errno)
    {
        errno = PosixFiles.Stat(path, followLink: false, out var stat);
        if (errno != 0) return null;

        string? target = null;
        bool toFolder = false;
        if (stat.Kind == WireKind.Symlink)
        {
            target = PosixFiles.ReadLink(path);
            // A dangling link points at no folder.
            toFolder = PosixFiles.Stat(path, followLink: true, out var pointed) == 0 && pointed.Kind == WireKind.Directory;
        }

        return new WireEntry(name, stat.Kind, stat.Size, stat.ModifiedTicks)
        {
            CreatedTicks = stat.CreatedTicks,
            Mode = stat.Mode,
            HasOwner = stat.HasIds,
            Uid = stat.Uid,
            Gid = stat.Gid,
            Owner = stat.HasIds ? accounts.User(stat.Uid) : null,
            Group = stat.HasIds ? accounts.Group(stat.Gid) : null,
            LinkTarget = target,
            LinkToDirectory = toFolder,
        };
    }
}
