using Nexaflow.Visuals.Common.Formatting;

namespace Nexaflow.Features.Email.ViewModels;

/// <summary>One row in the attachment strip. <see cref="EntryName"/> is the VFS entry name used to open the
/// part; <see cref="DisplayName"/> is what the user sees.</summary>
internal sealed class EmailAttachmentItem(string entryName, string displayName, string contentType, long size)
{
    public string EntryName { get; } = entryName;
    public string DisplayName { get; } = displayName;
    public string ContentType { get; } = contentType;
    public long Size { get; } = size;
    public string SizeText => SizeFormatter.FormatBytes(Size);
}
