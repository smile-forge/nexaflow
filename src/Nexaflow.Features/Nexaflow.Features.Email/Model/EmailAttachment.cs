namespace Nexaflow.Features.Email.Model;

/// <summary>
/// One decoded email part exposed as a file. <see cref="EntryName"/> is the stable, collision-free,
/// forward-slash VFS entry name (what <c>HandleObject(emlPath + EntryName)</c> resolves to and what
/// <see cref="Nexaflow.IO.Common.VirtualEntry.Name"/> carries); <see cref="DisplayName"/> is the original
/// filename for the UI. The <b>same</b> instance backs both the VFS session's entry list and the view-model's
/// attachment strip, so index <i>n</i> means the same part on both sides.
/// </summary>
internal sealed class EmailAttachment
{
    public required string EntryName { get; init; }
    public required string DisplayName { get; init; }
    public required string ContentType { get; init; }

    /// <summary>Decoded bytes. Held in memory because the parsed <see cref="EmailDocument"/> is cached and
    /// (for <c>.eml</c>) MimeKit already buffers part content on a non-persistent load.</summary>
    public required byte[] Content { get; init; }

    public long Size => Content.LongLength;

    /// <summary>True for a part rendered inside the body (a <c>cid:</c> image or a <c>Content-Disposition:
    /// inline</c> part) rather than a user-facing attachment. Inline parts are still entries.</summary>
    public bool IsInline { get; init; }

    /// <summary>The MIME Content-ID (angle brackets stripped), for matching <c>cid:</c> references in the
    /// HTML body. Null when the part has none.</summary>
    public string? ContentId { get; init; }
}
