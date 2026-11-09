namespace Nexaflow.IO.Common.Locations;

/// <summary>
/// Publishes a location of a feature's own on a runtime's file system: a tree it serves for as long as it is open — the
/// pictures of a document a tab has open, say — routed at <c>::{id}\…</c> as a connected location is, so whatever reads
/// and writes files reaches it through the file system and needs to know nothing of what serves it.
/// <para>
/// A published location is a <see cref="VirtualBacking.Remote"/> path like any other: nothing of it is on this disk, so an
/// action that would hand it to another program is not offered, and it is read and written through the async members.
/// </para>
/// </summary>
public interface ILocationPublisher
{
    /// <summary>
    /// Routes <paramref name="location"/>'s paths until the handle returned is disposed. Throws
    /// <see cref="ArgumentException"/> when its id is not path-segment-safe, or already names a mount or a location.
    /// </summary>
    IDisposable Publish(LocationMount location);
}
