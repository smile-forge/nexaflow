namespace Nexaflow.IO.Common.Locations;

/// <summary>What went wrong, in the terms every page reports a failure in — a dropped share reads the same everywhere.</summary>
public enum LocationError
{
    NotFound,
    AccessDenied,

    /// <summary>The link to the host dropped. The operation may succeed once the session reconnects.</summary>
    Disconnected,

    AuthFailed,

    /// <summary>The host presented a key other than the one on record. Never retried and never accepted in the
    /// same dialog: it is what a man-in-the-middle looks like.</summary>
    HostKeyChanged,

    /// <summary>The target exists, or changed since it was read, so the write was not made.</summary>
    Conflict,
}

/// <summary>
/// The one failure type the async VFS and every location provider throw for an expected failure. An
/// <see cref="System.IO.IOException"/>, so code that already catches those keeps working.
/// </summary>
public sealed class LocationException : System.IO.IOException
{
    public LocationException(LocationError error, string? path, string message, System.Exception? inner = null)
        : base(message, inner)
    {
        Error = error;
        Path  = path;
    }

    public LocationError Error { get; }

    /// <summary>The path the operation was given, or null when the failure is the connection's rather than a path's.</summary>
    public string? Path { get; }
}

/// <summary>
/// Thrown by a synchronous <see cref="IVirtualFileSystem"/> member handed a path on a remote location. A remote
/// operation waits on the network, so it is only offered in the async form — this names the member to move off.
/// A programming error, not a runtime condition: callers fix the call rather than catch it.
/// </summary>
public sealed class LocationRequiresAsyncException : System.InvalidOperationException
{
    public LocationRequiresAsyncException(string path, string member)
        : base($"{member} is synchronous and '{path}' is on a remote location; call its async counterpart.")
    {
        Path   = path;
        Member = member;
    }

    public string Path { get; }

    public string Member { get; }
}
