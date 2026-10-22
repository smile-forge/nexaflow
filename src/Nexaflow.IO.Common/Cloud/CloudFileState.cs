namespace Nexaflow.IO.Common.Cloud;

/// <summary>
/// Where a file in a cloud-synced folder stands, as Explorer's Status column shows it. <see cref="None"/> for a file no sync
/// provider owns, or one nothing is known about yet.
/// </summary>
public enum CloudFileState
{
    None,

    /// <summary>Only in the cloud: opening it downloads it.</summary>
    OnlineOnly,

    /// <summary>Downloaded, and in step with the cloud; the provider may free the space again.</summary>
    Available,

    /// <summary>Kept on this device for good ("Always keep on this device").</summary>
    AlwaysAvailable,

    /// <summary>Changed here or there and not yet reconciled, with no transfer under way.</summary>
    SyncPending,

    PendingUpload,
    PendingDownload,
    Transferring,

    /// <summary>The provider could not sync it.</summary>
    Error,

    /// <summary>The provider has something to say about it short of an error.</summary>
    Warning,

    /// <summary>In the synced folder, but the provider leaves it alone.</summary>
    Excluded,
}

/// <summary>Who besides its owner a cloud file is shared with, as far as its provider says.</summary>
public enum CloudSharing
{
    None,
    Shared,
    Public,
}

/// <summary>What a cloud file's sync provider says of it — read from the provider, one file at a time.</summary>
public sealed record CloudFileStatus(CloudFileState State, CloudSharing Sharing);
