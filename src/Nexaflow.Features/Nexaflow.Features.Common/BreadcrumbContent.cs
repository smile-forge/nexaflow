namespace Nexaflow.Features.Common;

/// <summary>
/// What a crumb stands for (<see cref="BreadcrumbSegment.Content"/>), for the display filters to read
/// (<see cref="IBreadcrumbDisplayFilter"/>). A page says what the crumb is; how it reads on screen is the shell's,
/// restated by every filter that knows that kind of content. Derive a record of your own for content no shared kind
/// describes.
/// </summary>
public abstract record BreadcrumbContent;

/// <summary>
/// A place in the runtime's file system — a folder or a file — by the path the file system names it, which is not
/// always how a person names it: a distro's folder is <c>::wsl-ubuntu\etc</c> to the file system and
/// <c>wsl://Ubuntu/etc</c> to the person.
/// </summary>
/// <param name="Path">The place, as <c>IVirtualFileSystem</c> names it.</param>
/// <param name="Naming">How the page labelled the crumb, so a filter restates the label in the same form.</param>
public sealed record PlaceContent(string Path, PlaceNaming Naming) : BreadcrumbContent;

/// <summary>How a page labelled a <see cref="PlaceContent"/> crumb.</summary>
public enum PlaceNaming
{
    /// <summary>The label is the whole path (a viewer's folder crumb, <c>D:\temp</c>); a filter restates it as the
    /// place's parts, which the shell can shorten from the middle (<c>D:\…\temp</c>).</summary>
    WholePath,

    /// <summary>The label is the place's own name (one folder of the browser's trail); a filter restates the name.</summary>
    OwnName,

    /// <summary>The page chose the label itself; a filter leaves it and restates only what is copied and the tooltip.</summary>
    PageLabel,
}
