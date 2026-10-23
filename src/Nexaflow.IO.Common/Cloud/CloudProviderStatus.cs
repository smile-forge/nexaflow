using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Nexaflow.IO.Common.Cloud;

/// <summary>
/// What a file's sync provider says of it — the state behind Explorer's Status column icon (<c>System.StorageProviderState</c>)
/// and its sharing (<c>System.StorageProviderSharingStatus</c>) — read through the shell's property system. That costs the
/// provider's own handler a call per file (tens of milliseconds for OneDrive), so it is for the rows a reader can see,
/// off the UI thread; the listing's <see cref="CloudPlaceholders.Classify"/> is what every row shows first.
/// </summary>
public static class CloudProviderStatus
{
    private static readonly PropertyKey StateKey   = new(new Guid("E77E90DF-6271-4F5B-834F-2DD1F245DDA4"), 3);
    private static readonly PropertyKey SharingKey = new(new Guid("FCEFF153-E839-4CF3-A9E7-EA22832094B8"), 117);
    private static readonly PropertyKey[] Keys     = [StateKey, SharingKey];

    private const ushort VtUi4 = 19;

    /// <summary>
    /// The provider's word on <paramref name="path"/>, or null where the shell has none (no provider owns it, or it is gone).
    /// Call from a COM-initialised thread; the provider's handler may be apartment-threaded.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static CloudFileStatus? Read(string path)
    {
        IShellItem2? item = null;
        IPropertyStore? store = null;
        try
        {
            var itemId = typeof(IShellItem2).GUID;
            if (SHCreateItemFromParsingName(path, IntPtr.Zero, ref itemId, out item) != 0 || item is null) return null;

            var storeId = typeof(IPropertyStore).GUID;
            if (item.GetPropertyStoreForKeys(Keys, (uint)Keys.Length, 0 /* GPS_DEFAULT */, ref storeId, out store) != 0 || store is null)
                return null;

            uint? state = ReadUInt32(store, StateKey);
            if (state is null) return null;
            return new CloudFileStatus(StateFrom(state.Value), SharingFrom(ReadUInt32(store, SharingKey) ?? 0));
        }
        catch (COMException)
        {
            return null;
        }
        finally
        {
            if (store is not null) Marshal.ReleaseComObject(store);
            if (item is not null) Marshal.ReleaseComObject(item);
        }
    }

    /// <summary>The state a <c>STORAGEPROVIDERSTATE_*</c> value names.</summary>
    public static CloudFileState StateFrom(uint providerState) => providerState switch
    {
        1  => CloudFileState.OnlineOnly,        // SPARSE
        2  => CloudFileState.Available,         // IN_SYNC
        3  => CloudFileState.AlwaysAvailable,   // PINNED
        4  => CloudFileState.PendingUpload,
        5  => CloudFileState.PendingDownload,
        6  => CloudFileState.Transferring,
        7  => CloudFileState.Error,
        8  => CloudFileState.Warning,
        9  => CloudFileState.Excluded,
        10 => CloudFileState.SyncPending,       // PENDING_UNSPECIFIED
        _  => CloudFileState.None,
    };

    /// <summary>The sharing a <c>STORAGE_PROVIDER_SHARINGSTATUS_*</c> value names; private counts as not shared.</summary>
    public static CloudSharing SharingFrom(uint sharingStatus) => sharingStatus switch
    {
        1 or 4 or 5 => CloudSharing.Shared,     // SHARED, SHARED_OWNED, SHARED_COOWNED
        3 or 6 or 7 => CloudSharing.Public,     // PUBLIC, PUBLIC_OWNED, PUBLIC_COOWNED
        _           => CloudSharing.None,
    };

    private static uint? ReadUInt32(IPropertyStore store, PropertyKey key)
    {
        if (store.GetValue(ref key, out var value) != 0) return null;
        try { return value.Type == VtUi4 ? value.UInt32 : null; }
        finally { PropVariantClear(ref value); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey(Guid formatId, uint propertyId)
    {
        public Guid FormatId = formatId;
        public uint PropertyId = propertyId;
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PropVariant
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public uint UInt32;
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
    }

    /// <summary>IShellItem2, declared only as far as <c>GetPropertyStoreForKeys</c>: the slots before it keep the vtable in place.</summary>
    [ComImport, Guid("7E9FB0D3-919F-4307-AB2E-9B1860310C93"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem2
    {
        void BindToHandler();
        void GetParent();
        void GetDisplayName();
        void GetAttributes();
        void Compare();
        void GetPropertyStore();
        void GetPropertyStoreWithCreateObject();

        [PreserveSig]
        int GetPropertyStoreForKeys([In, MarshalAs(UnmanagedType.LPArray)] PropertyKey[] keys, uint count, int flags,
                                    ref Guid storeId, [MarshalAs(UnmanagedType.Interface)] out IPropertyStore? store);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(string path, IntPtr bindContext, ref Guid itemId,
                                                          [MarshalAs(UnmanagedType.Interface)] out IShellItem2? item);

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant value);
}
