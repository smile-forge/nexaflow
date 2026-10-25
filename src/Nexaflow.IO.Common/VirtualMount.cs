namespace Nexaflow.IO.Common;

/// <summary>
/// How a path's bytes are obtained — and therefore what may safely be done with it.
/// <para>
/// The distinction matters because materialising an archive entry produces a lone temp file: its
/// NEIGHBOURS (dependencies, sidecars, an installer's payload) do not exist on disk. Handing such a
/// path to an external process is a broken promise, so an action that needs them is suppressed rather
/// than served a half-truth. Under a mount the whole subtree really is on disk, so the same action is
/// safe once the path is resolved.
/// </para>
/// </summary>
public enum VirtualBacking
{
    /// <summary>An ordinary Windows path.</summary>
    Real,

    /// <summary>Under a pass-through mount: the whole subtree exists on disk at the mapped location,
    /// so the resolved real path behaves exactly like <see cref="Real"/>.</summary>
    PassThrough,

    /// <summary>Inside an archive: only the single requested file can be produced, to a temp copy.</summary>
    Materialized,

    /// <summary>On a remote location: nothing is on disk, and the bytes arrive over the network through the
    /// async members. An action that would hand the path to an external process is suppressed, as for an
    /// archive entry.</summary>
    Remote,
}

/// <summary>Reading a <see cref="VirtualBacking"/>.</summary>
public static class VirtualBackings
{
    /// <summary>True when the path's whole subtree is on disk, so its resolved real path may be handed to an
    /// external process: <see cref="VirtualBacking.Real"/> or <see cref="VirtualBacking.PassThrough"/>.</summary>
    public static bool IsFullyOnDisk(this VirtualBacking backing)
        => backing is VirtualBacking.Real or VirtualBacking.PassThrough;

    /// <summary>Where <paramref name="path"/> is on this disk, when the whole of it is — null for a path in an archive or on a
    /// location. What a reader that has to open the file by its own path, or may read the disk directly, goes by.</summary>
    public static string? ResolveOnDisk(this IVirtualFileSystem vfs, string path)
        => vfs.GetBacking(path).IsFullyOnDisk() ? vfs.TryResolveReal(path) : null;
}

/// <summary>
/// A virtual root mapped onto a real directory. Paths beneath <c>::{Id}</c> resolve to
/// <see cref="RealRoot"/> by plain string substitution — no extraction, no temp copy — so a mounted
/// location is exactly as capable as the directory behind it while never revealing where that is.
/// </summary>
/// <param name="Id">Stable, path-segment-safe key forming the virtual root <c>::{Id}</c>. Never the
/// display label: it is baked into saved tab state, so renaming must not invalidate it.</param>
/// <param name="Label">Friendly name shown in breadcrumbs in place of the id.</param>
/// <param name="RealRoot">The real directory the mount maps onto.</param>
public sealed record VirtualMount(string Id, string Label, string RealRoot)
{
    /// <summary>The virtual-root marker. Two colons can never begin a real Windows path, so
    /// <c>File.Exists</c> and <c>Directory.Exists</c> answer false for a mounted path without touching
    /// the disk — which is what makes it safe to test the prefix before anything else.</summary>
    public const string Prefix = "::";

    /// <summary>The virtual root path for a mount id, i.e. <c>::{id}</c>.</summary>
    public static string RootFor(string id) => Prefix + id;
}
