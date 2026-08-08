namespace Nexaflow.IO.Pe;

/// <summary>How badly a parse step failed. Nothing here aborts the read.</summary>
public enum PeSeverity
{
    /// <summary>Worth surfacing, but the structure parsed — e.g. a truncated string table.</summary>
    Info,
    /// <summary>The structure is malformed but a partial result survived.</summary>
    Warning,
    /// <summary>The structure could not be read at all; its model property is null/empty.</summary>
    Error,
}

/// <summary>
/// One thing that went wrong while reading an image. <see cref="PeReader"/> never throws — a
/// packed, truncated or deliberately corrupted binary is the normal case for an inspector, not an
/// exceptional one, so every failure lands here and the caller still gets everything that did parse.
/// </summary>
/// <param name="Severity">How much was lost.</param>
/// <param name="Area">Which structure — "OptionalHeader", "Imports", "Resources", …</param>
/// <param name="Message">Human-readable detail, shown verbatim in the inspector.</param>
/// <param name="Offset">File offset the failure was detected at, when known.</param>
public sealed record PeDiagnostic(PeSeverity Severity, string Area, string Message, long? Offset = null)
{
    public override string ToString()
        => Offset is { } o ? $"{Severity}: [{Area}] {Message} (at 0x{o:X})" : $"{Severity}: [{Area}] {Message}";
}
