namespace Nexaflow.WslHelper.Native;

/// <summary>The errno values the helper tells apart, in the generic Linux numbering x86-64 and arm64 share.</summary>
internal static class Errno
{
    public const int EPERM = 1;
    public const int ENOENT = 2;
    public const int EINTR = 4;
    public const int EAGAIN = 11;
    public const int EACCES = 13;
    public const int EEXIST = 17;
    public const int EXDEV = 18;
    public const int ENOTDIR = 20;
    public const int EISDIR = 21;
    public const int EINVAL = 22;
    public const int ENOSPC = 28;
    public const int ENOSYS = 38;
    public const int ENOTEMPTY = 39;
}
