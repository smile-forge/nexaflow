namespace Nexaflow.Features.Common;

/// <summary>
/// What of the disk a page puts in front of the assistant (<see cref="IPageViewModel.GetContextGrant"/>): files it shows
/// one by one — an open picture, a document — and folders it shows whole, each with everything under it and nothing
/// above it. The assistant's tools reach paths inside these and no others.
/// </summary>
public sealed record ContextGrant(IReadOnlyList<string> Files, IReadOnlyList<string> Folders)
{
    /// <summary>Nothing from the disk.</summary>
    public static ContextGrant None { get; } = new([], []);

    /// <summary>One file a page shows; nothing where it shows none yet.</summary>
    public static ContextGrant OfFile(string? path) => string.IsNullOrWhiteSpace(path) ? None : new([path], []);

    /// <summary>The files a page shows one by one.</summary>
    public static ContextGrant OfFiles(IEnumerable<string> paths) => new([.. paths.Where(p => !string.IsNullOrWhiteSpace(p))], []);

    /// <summary>A folder a page shows whole, and everything under it; nothing where it shows none yet.</summary>
    public static ContextGrant OfFolder(string? path) => string.IsNullOrWhiteSpace(path) ? None : new([], [path]);

    /// <summary>Folders a page shows whole.</summary>
    public static ContextGrant OfFolders(IEnumerable<string> paths) => new([], [.. paths.Where(p => !string.IsNullOrWhiteSpace(p))]);

    /// <summary>This and <paramref name="other"/> together.</summary>
    public ContextGrant With(ContextGrant other) => new([.. Files, .. other.Files], [.. Folders, .. other.Folders]);

    public bool Equals(ContextGrant? other)
        => other is not null && Files.SequenceEqual(other.Files, StringComparer.OrdinalIgnoreCase) && Folders.SequenceEqual(other.Folders, StringComparer.OrdinalIgnoreCase);

    public override int GetHashCode() => HashCode.Combine(Files.Count, Folders.Count);
}
