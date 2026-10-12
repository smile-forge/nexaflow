using System.Collections.Generic;

namespace Nexaflow.Features.Common
{
    /// <summary>
    /// A kind of file that belongs to another and is named after it: the other file's whole name plus
    /// <see cref="Suffix"/> — <c>photo.jpg</c> and its edit recipe <c>photo.jpg.nfedit</c>.
    /// </summary>
    /// <param name="Suffix">Appended to the primary file's name, dot included (<c>.nfedit</c>).</param>
    /// <param name="Badge">A glyph shown on the primary while it has this companion; null shows nothing.</param>
    /// <param name="BadgeTooltip">What the badge means, in the reader's language.</param>
    public sealed record FileCompanionKind(string Suffix, string? Badge = null, string? BadgeTooltip = null);

    /// <summary>
    /// A feature's declaration that some files are companions of others. The file browser then treats a primary and
    /// its companions as one: a companion is hidden while its primary is beside it, the primary carries the badge,
    /// and copying, moving, renaming and deleting the primary take its companions along — renamed to follow it.
    /// </summary>
    /// <remarks>
    /// Discovered by the file browser like its other contracts (via <c>IShellServices.DiscoverImplementations</c>) and
    /// made with a parameterless constructor. A declaration, not behaviour: what the files mean stays the feature's.
    /// A companion whose primary is gone is shown as an ordinary file, so it can be found and removed.
    /// </remarks>
    public interface IFileCompanionProvider
    {
        IReadOnlyList<FileCompanionKind> Companions { get; }
    }
}
