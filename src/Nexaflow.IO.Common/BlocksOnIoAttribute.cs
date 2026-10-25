using System;

namespace Nexaflow.IO.Common;

/// <summary>
/// Marks a member of <see cref="IVirtualFileSystem"/> that waits on the disk or the network before it returns. A feature
/// calling one is reported (<c>NXIO001</c>), because on a remote location the wait is a round trip on whatever thread
/// asked — the UI thread, more often than not — and a runtime's file system refuses the call there outright with
/// <see cref="Locations.LocationRequiresAsyncException"/>. The report names <see cref="Alternative"/>, the member to await
/// instead.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class BlocksOnIoAttribute : Attribute
{
    /// <param name="alternative">The awaitable member that does the same work, or null when there is none and the
    /// member is for local paths only.</param>
    public BlocksOnIoAttribute(string? alternative) => Alternative = alternative;

    /// <summary>The awaitable member that does the same work; null when the member is for local paths only.</summary>
    public string? Alternative { get; }
}
