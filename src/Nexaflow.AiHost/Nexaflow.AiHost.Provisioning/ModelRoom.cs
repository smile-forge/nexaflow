namespace Nexaflow.AiHost.Provisioning;

/// <summary>
/// What fetching a set of model files takes on the asset store's drive at its fullest — every file still to come, each
/// archive staged beside the entries it is unpacked into, and whatever downloads already under way still need — against
/// what the drive has left. A drive that will not say how much it has left never refuses a download.
/// </summary>
/// <param name="Bytes">The room the fetch needs, downloads under way included.</param>
/// <param name="Free">What the drive has left for this user; null when it would not say.</param>
/// <param name="Drive">The drive the store is on, as it is named to a person: <c>C:\</c>.</param>
public sealed record ModelRoom(long Bytes, long? Free, string Drive)
{
    public bool Fits => Free is not { } free || Bytes <= free;

    /// <summary>How much more room the fetch needs than the drive has; 0 when it fits.</summary>
    public long Shortfall => Fits ? 0 : Bytes - Free!.Value;
}

/// <summary>A model download refused before a byte of it came: the drive the asset store is on has too little room left.</summary>
public sealed class ModelRoomException(ModelRoom room)
    : IOException($"Fetching these files needs {room.Bytes:N0} bytes on {room.Drive}, which has {room.Free ?? 0:N0} left.")
{
    public ModelRoom Room => room;
}
