using System.Runtime.InteropServices;
using Nexaflow.WslHelper.Native;

namespace Nexaflow.WslHelper.Files;

/// <summary>
/// The names behind uids and gids, read from the distro's account database once each. <c>getpwuid</c> and
/// <c>getgrgid</c> answer in storage the next call reuses, so every call is made under one lock.
/// </summary>
internal sealed class AccountNames
{
    // Offsets of pw_name (gr_name likewise), pw_dir and pw_shell: one layout in glibc and musl on 64-bit Linux.
    private const int NameAt = 0, HomeAt = 32, ShellAt = 40;

    private readonly Lock _gate = new();
    private readonly Dictionary<uint, string?> _users = [];
    private readonly Dictionary<uint, string?> _groups = [];

    /// <summary>The user a uid names, or null when it names nobody.</summary>
    public string? User(uint uid)
    {
        lock (_gate)
        {
            if (!_users.TryGetValue(uid, out string? name)) _users[uid] = name = Text(Libc.GetPasswdByUid(uid), NameAt);
            return name;
        }
    }

    /// <summary>The group a gid names, or null when it names none.</summary>
    public string? Group(uint gid)
    {
        lock (_gate)
        {
            if (!_groups.TryGetValue(gid, out string? name)) _groups[gid] = name = Text(Libc.GetGroupByGid(gid), NameAt);
            return name;
        }
    }

    /// <summary>The home folder and login shell of a uid, each null where the database holds none.</summary>
    public (string? Home, string? Shell) Login(uint uid)
    {
        lock (_gate)
        {
            nint record = Libc.GetPasswdByUid(uid);
            return (Text(record, HomeAt), Text(record, ShellAt));
        }
    }

    private static string? Text(nint record, int offset)
        => record != 0 && Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(record, offset)) is { Length: > 0 } text ? text : null;
}
