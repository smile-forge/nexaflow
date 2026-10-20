using System.IO;
using Nexaflow.IO.Common;
using Nexaflow.Visuals.Common.Formatting;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Core.Services;

/// <summary>
/// What fetching a set of model files takes on the asset store's drive at its fullest — every file still to come, each
/// archive staged beside the entries it is unpacked into, and whatever downloads already under way still need — against
/// what the drive has left. A drive that will not say how much it has left never refuses a download.
/// </summary>
/// <param name="Bytes">The room the fetch needs, downloads under way included.</param>
/// <param name="Free">What the drive has left for this user; null when it would not say.</param>
/// <param name="Drive">The drive the store is on, as it is named to the user: <c>C:\</c>.</param>
public sealed record ModelRoom(long Bytes, long? Free, string Drive)
{
    public static ModelRoom Of(SaveRoom room) => new(room.Bytes, room.Free, Path.GetPathRoot(room.Target) ?? room.Target);

    public bool Fits => Free is not { } free || Bytes <= free;

    /// <summary>How much more room the fetch needs than the drive has; 0 when it fits.</summary>
    public long Shortfall => Fits ? 0 : Bytes - Free!.Value;

    /// <summary>Why the fetch is refused, as the user reads it: what it needs, and what is free where.</summary>
    public string Refusal => Str.Format("Shell.Ai.Room.ShortFormat", SizeFormatter.FormatBytes(Bytes), SizeFormatter.FormatBytes(Free ?? 0), Drive);
}

/// <summary>A model download refused before a byte of it came: the drive the asset store is on has too little room left.</summary>
public sealed class ModelRoomException(ModelRoom room) : IOException(room.Refusal)
{
    public ModelRoom Room => room;
}
