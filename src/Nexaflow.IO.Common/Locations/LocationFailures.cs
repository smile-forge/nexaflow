namespace Nexaflow.IO.Common.Locations;

/// <summary>
/// Turns the BCL's failures that have a common meaning into the <see cref="LocationException"/> family, so a page
/// reports a missing file on <c>C:\</c> the way it reports one on a server. Anything without a common meaning — a
/// sharing violation, a full disk — passes through unchanged.
/// </summary>
internal static class LocationFailures
{
    // ERROR_FILE_EXISTS, ERROR_ALREADY_EXISTS and ERROR_DIR_NOT_EMPTY, as the low word of an IOException's HResult.
    private const int FileExists    = 80;
    private const int AlreadyExists = 183;
    private const int DirNotEmpty   = 145;

    /// <summary>The <see cref="LocationException"/> <paramref name="failure"/> stands for, or null when it has none
    /// (and when it already is one), so a <c>when</c> filter rethrows only what it translated.</summary>
    public static LocationException? Translate(System.Exception failure, string path) => failure switch
    {
        LocationException => null,
        FileNotFoundException or DirectoryNotFoundException
            => new LocationException(LocationError.NotFound, path, failure.Message, failure),
        System.UnauthorizedAccessException
            => new LocationException(LocationError.AccessDenied, path, failure.Message, failure),
        IOException io when (io.HResult & 0xFFFF) is FileExists or AlreadyExists or DirNotEmpty
            => new LocationException(LocationError.Conflict, path, failure.Message, failure),
        _ => null,
    };

    public static LocationException Conflict(string path, string message)
        => new(LocationError.Conflict, path, message);

    public static LocationException NotFound(string path)
        => new(LocationError.NotFound, path, $"Nothing is at '{path}'.");
}
