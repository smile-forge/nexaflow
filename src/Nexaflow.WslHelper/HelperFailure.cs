using System.Runtime.InteropServices;
using Nexaflow.IO.Location.Wsl.Protocol;
using Nexaflow.WslHelper.Native;

namespace Nexaflow.WslHelper;

/// <summary>A request that failed for a reason the backend tells apart; its message is what a person reads.</summary>
internal sealed class HelperFailure(WslFailure failure, string message) : Exception(message)
{
    public WslFailure Failure { get; } = failure;

    /// <summary>The failure an errno names, met at <paramref name="path"/>.</summary>
    public static HelperFailure FromErrno(int errno, string path)
        => new(Classify(errno), $"{path}: {Marshal.GetPInvokeErrorMessage(errno)}");

    /// <summary>The failure an exception names: its own when it is one, else by its type, or by the errno an
    /// <see cref="IOException"/> carries on Linux as its HResult.</summary>
    public static HelperFailure From(Exception exception) => exception switch
    {
        HelperFailure failure => failure,
        FileNotFoundException or DirectoryNotFoundException => new(WslFailure.NotFound, exception.Message),
        UnauthorizedAccessException => new(WslFailure.AccessDenied, exception.Message),
        IOException { HResult: > 0 } io => new(Classify(io.HResult), io.Message),
        _ => new(WslFailure.Other, exception.Message),
    };

    public static WslFailure Classify(int errno) => errno switch
    {
        Errno.ENOENT or Errno.ENOTDIR => WslFailure.NotFound,
        Errno.EACCES or Errno.EPERM => WslFailure.AccessDenied,
        Errno.EEXIST or Errno.EISDIR => WslFailure.Conflict,
        Errno.ENOTEMPTY => WslFailure.NotEmpty,
        _ => WslFailure.Other,
    };
}
