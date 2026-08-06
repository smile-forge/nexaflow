using CommunityToolkit.Mvvm.ComponentModel;
using Nexaflow.Features.Email.Model;
using Nexaflow.Visuals.Common.Formatting;

namespace Nexaflow.Features.Email.ViewModels;

/// <summary>One row in the attachment strip. <see cref="EntryName"/> is the VFS entry name used to open the
/// part; <see cref="DisplayName"/> is what the user sees. Wraps the decoded <see cref="EmailAttachment"/> so a
/// "?" search can look inside a text attachment (or a nested message) and light up the tile that holds a match.</summary>
internal sealed partial class EmailAttachmentItem(EmailAttachment source) : ObservableObject
{
    /// <summary>The decoded part behind this row — its bytes back an in-attachment content search.</summary>
    internal EmailAttachment Source { get; } = source;

    public string EntryName   => Source.EntryName;
    public string DisplayName => Source.DisplayName;
    public string ContentType => Source.ContentType;
    public long   Size        => Source.Size;
    public string SizeText    => SizeFormatter.FormatBytes(Size);

    /// <summary>True while a search matched this attachment (its name, its text, or a nested message) — the
    /// tile glows so the user sees which attachment the hit is in.</summary>
    [ObservableProperty] private bool _highlighted;
}
