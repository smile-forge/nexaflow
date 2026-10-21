using System.IO;
using System.Runtime.InteropServices;

namespace Nexaflow.IO.Common.Cloud;

/// <summary>The cloud-files placeholder states Windows reports (<c>CF_PLACEHOLDER_STATE</c>).</summary>
[Flags]
public enum CloudPlaceholderFlags : uint
{
    None                 = 0x00,
    Placeholder          = 0x01,
    SyncRoot             = 0x02,
    EssentialPropPresent = 0x04,
    InSync               = 0x08,
    Partial              = 0x10,
    PartiallyOnDisk      = 0x20,
}

/// <summary>
/// The Cloud Files API's view of a directory listing. A sync provider's files are placeholders whose state travels in each
/// entry's attributes and reparse tag, so a listing names every row's state with no further call per file — provided the
/// listing thread asks to see placeholders as they are (Windows disguises them as ordinary files to everyone else).
/// </summary>
public static class CloudPlaceholders
{
    private const sbyte ExposePlaceholders = 2;   // PHCM_EXPOSE_PLACEHOLDERS

    private const FileAttributes Pinned             = (FileAttributes)0x00080000;
    private const FileAttributes RecallOnOpen       = (FileAttributes)0x00040000;
    private const FileAttributes RecallOnDataAccess = (FileAttributes)0x00400000;

    /// <summary>False on a Windows older than the per-thread placeholder switch (1803), where listings stay disguised.</summary>
    private static bool _canExpose = true;

    /// <summary>
    /// Shows placeholders as they are to directory reads on this thread until disposed, then puts the thread back as it
    /// was. Held only around each read: a listing that resumes on another thread switches that one in its turn.
    /// </summary>
    internal static PlaceholderExposure ExposeOnThisThread()
    {
        if (!_canExpose) return default;
        try
        {
            sbyte previous = RtlSetThreadPlaceholderCompatibilityMode(ExposePlaceholders);
            return previous < 0 ? default : new PlaceholderExposure(previous);
        }
        catch (EntryPointNotFoundException)
        {
            _canExpose = false;
            return default;
        }
    }

    /// <summary>The placeholder states an entry's attributes and reparse tag carry, as its directory listing gave them.</summary>
    internal static CloudPlaceholderFlags FlagsOf(FileAttributes attributes, uint reparseTag)
    {
        if ((attributes & FileAttributes.ReparsePoint) == 0) return CloudPlaceholderFlags.None;
        uint flags = CfGetPlaceholderStateFromAttributeTag((uint)attributes, reparseTag);
        return flags == uint.MaxValue ? CloudPlaceholderFlags.None : (CloudPlaceholderFlags)flags;
    }

    /// <summary>
    /// What a listing alone can say of a file: whether it is here at all, kept here for good, and in step with the cloud.
    /// An error, a transfer under way or a sharing comes only from its provider (<see cref="CloudProviderStatus"/>).
    /// </summary>
    public static CloudFileState Classify(FileAttributes attributes, CloudPlaceholderFlags flags)
    {
        if ((flags & CloudPlaceholderFlags.Placeholder) == 0) return CloudFileState.None;

        bool pinned = (attributes & Pinned) != 0;
        bool notHere = (flags & CloudPlaceholderFlags.PartiallyOnDisk) != 0
                    || (attributes & (RecallOnDataAccess | RecallOnOpen | FileAttributes.Offline)) != 0;

        // Kept for good but not here yet: the provider is bringing it down.
        if (notHere) return pinned ? CloudFileState.PendingDownload : CloudFileState.OnlineOnly;
        if ((flags & CloudPlaceholderFlags.InSync) == 0) return CloudFileState.SyncPending;
        return pinned ? CloudFileState.AlwaysAvailable : CloudFileState.Available;
    }

    /// <summary>Whether <paramref name="path"/> lies inside a folder a sync provider has registered. Opens the path, so never on the UI thread.</summary>
    public static bool IsInSyncRoot(string path)
    {
        try { return CfGetSyncRootInfoByPath(path, 0 /* CF_SYNC_ROOT_INFO_BASIC */, out _, sizeof(long), out _) == 0; }
        catch (DllNotFoundException) { return false; }
        catch (EntryPointNotFoundException) { return false; }
    }

    /// <summary>Restores the thread's placeholder view when the read it wrapped is done.</summary>
    internal readonly struct PlaceholderExposure : IDisposable
    {
        private readonly sbyte _previous;
        private readonly bool _changed;

        internal PlaceholderExposure(sbyte previous)
        {
            _previous = previous;
            _changed  = previous != ExposePlaceholders;
        }

        public void Dispose()
        {
            if (_changed) RtlSetThreadPlaceholderCompatibilityMode(_previous);
        }
    }

    [DllImport("ntdll.dll")]
    private static extern sbyte RtlSetThreadPlaceholderCompatibilityMode(sbyte mode);

    [DllImport("cldapi.dll")]
    private static extern uint CfGetPlaceholderStateFromAttributeTag(uint fileAttributes, uint reparseTag);

    [DllImport("cldapi.dll", CharSet = CharSet.Unicode)]
    private static extern int CfGetSyncRootInfoByPath(string filePath, int infoClass, out long infoBuffer, uint infoBufferLength, out uint returnedLength);
}
