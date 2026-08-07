using Nexaflow.Features.Common;
using Nexaflow.Features.OneDrive.Controls;

namespace Nexaflow.Features.OneDrive;

/// <summary>How the user wants a detected account shown, keyed by its stable id.</summary>
/// <param name="Id">The detected account's id. An override for an id that no longer turns up is simply
/// ignored — it must never resurrect a row for an account that has been signed out.</param>
/// <param name="Label">A replacement name, or null to keep the detected one.</param>
/// <param name="Hidden">Whether to leave it out of This PC entirely.</param>
public sealed record SyncFolderOverride(string Id, string? Label = null, bool Hidden = false);

/// <summary>A folder the user added by hand, for a location detection can't find.</summary>
public sealed record SyncFolderEntry(string Id, string Label, string FolderPath);

/// <summary>
/// OneDrive's settings: which detected sync folders to show, what to call them, and any extra folders
/// the user pointed us at. Nothing here is a credential — this feature reads the local machine only.
/// </summary>
[CustomControl(typeof(OneDriveOptionsControl))]
public sealed class OneDriveConfig : IFeatureConfig
{
    public string ConfigName   => "onedrive";
    public string FriendlyName => "OneDrive";

    /// <summary>Renames and hides for detected accounts, by id.</summary>
    public List<SyncFolderOverride> Overrides { get; set; } = [];

    /// <summary>Folders the user added. An "add" is picking an existing local folder and naming it —
    /// there is no cloud call behind it.</summary>
    public List<SyncFolderEntry> Custom { get; set; } = [];

    /// <summary>
    /// Raised by the Options editor once changes are applied, so an open This PC tab re-queries without
    /// the user having to navigate away and back.
    /// <para>
    /// The shell's own config→tab refresh can't reach this: it maps a config to the page registrations
    /// whose constructors take it, and the file browser's registration cannot reference another feature's
    /// config type. Not serialized — System.Text.Json ignores events.
    /// </para>
    /// </summary>
    public event Action? Changed;

    public void RaiseChanged() => Changed?.Invoke();
}
