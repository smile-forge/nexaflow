namespace Nexaflow.IO.Common.Locations;

/// <summary>
/// A connected remote location as the VFS routes to it: the connection, what it can do, and where its live session
/// comes from. Registered on a runtime's <see cref="LocationFileSystem"/>, it makes <c>::{Id}\…</c> paths reach the
/// host — the remote counterpart of <see cref="VirtualMount"/>, which maps onto a real directory instead.
/// </summary>
/// <param name="Capabilities">What the location can do, read synchronously by the UI before it offers an action —
/// the provider's <see cref="ILocationProvider.Capabilities"/>.</param>
public sealed record LocationMount(ConnectionProfile Connection, LocationCapabilities Capabilities, ILocationSessionSource Sessions)
{
    public string Id => Connection.Id;

    public string Label => Connection.Label;

    /// <summary>The virtual root, <c>::{Id}</c>.</summary>
    public string Root => VirtualMount.RootFor(Connection.Id);
}
