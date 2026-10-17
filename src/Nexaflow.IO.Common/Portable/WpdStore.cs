using System.Runtime.InteropServices;
using Nexaflow.IO.Common.Hardware;
using Nexaflow.IO.Common.Stores;
using ComStream = System.Runtime.InteropServices.ComTypes.IStream;

namespace Nexaflow.IO.Common.Portable;

/// <summary>
/// A phone's or camera's files over MTP, through Windows Portable Devices: its roots are the storages it shares
/// (internal storage, an SD card), and an id is a WPD object id.
/// </summary>
/// <remarks>
/// A phone shares nothing while it is locked or set to charge only; its storages appear when it is unlocked and allows
/// file transfer, which the device announces as an event — raised here as <see cref="Changed"/>, so whatever shows the
/// store reads its roots again. The device is opened through the free-threaded <c>PortableDeviceFTM</c>, so calls
/// may come from any thread.
/// </remarks>
public sealed class WpdStore : IFileStore
{
    const uint GenericRead = 0x80000000, GenericWrite = 0x40000000;
    const uint SecurityImpersonation = 0x20000;
    const uint DeleteWithRecursion = 1;
    const int AccessDenied = unchecked((int)0x80070005);
    const int EnumerateBatch = 64;
    const int BufferSize = 256 * 1024;

    readonly IPortableDevice _device;
    readonly IPortableDeviceContent _content;
    readonly IPortableDeviceProperties _properties;
    readonly IPortableDeviceResources _resources;
    readonly IPortableDeviceKeyCollection _listKeys;
    readonly Events _events;
    readonly string? _cookie;
    bool _disposed;

    WpdStore(IPortableDevice device, bool readOnly)
    {
        _device = device;
        IsReadOnly = readOnly;
        _device.Content(out _content);
        _content.Properties(out _properties);
        _content.Transfer(out _resources);
        _listKeys = Keys(WpdKeys.ObjectName, WpdKeys.ObjectOriginalFileName, WpdKeys.ObjectContentType, WpdKeys.ObjectSize,
            WpdKeys.ObjectDateModified, WpdKeys.ObjectIsHidden, WpdKeys.ObjectIsSystem);
        Name = DeviceName();
        Facts = DeviceFacts();
        _events = new Events(this);
        try { _device.Advise(0, _events, null, out var cookie); _cookie = cookie; }
        catch (COMException) { _cookie = null; }
    }

    /// <summary>Every portable device Windows has: phones and cameras over USB, and the WPD views it makes of other things.</summary>
    public static IReadOnlyList<string> DevicePaths() => DeviceInterfaces.Of(WpdGuids.DeviceInterface);

    /// <summary>The Windows Portable Devices paths of a devnode — an MTP function — usually one.</summary>
    public static IReadOnlyList<string> DevicePathsOf(string instanceId) =>
        [.. DeviceInterfaces.Of(WpdGuids.DeviceInterface)
            .Where(path => string.Equals(DeviceInterfaces.InstanceIdOf(path), instanceId, StringComparison.OrdinalIgnoreCase))];

    /// <summary>Opens the device for reading and writing, or for reading only where it allows no more.</summary>
    public static WpdStore Open(string devicePath)
    {
        var device = WpdGuids.Create<IPortableDevice>(WpdGuids.PortableDeviceFtm);
        try
        {
            device.Open(devicePath, ClientInfo(GenericRead | GenericWrite));
            return new WpdStore(device, readOnly: false);
        }
        catch (COMException e) when (e.HResult == AccessDenied)
        {
            device.Open(devicePath, ClientInfo(GenericRead));
            return new WpdStore(device, readOnly: true);
        }
    }

    public string Name { get; }

    /// <summary>Opened for reading only: the device would not let it write.</summary>
    public bool IsReadOnly { get; }

    public IReadOnlyDictionary<string, string> Facts { get; }

    public event EventHandler? Changed;

    public Task<IReadOnlyList<StoreRoot>> RootsAsync(CancellationToken ct) => Task.Run<IReadOnlyList<StoreRoot>>(() =>
    {
        var keys = Keys(WpdKeys.ObjectName, WpdKeys.ObjectContentType, WpdKeys.FunctionalObjectCategory, WpdKeys.StorageDescription,
            WpdKeys.StorageFileSystemType, WpdKeys.StorageCapacity, WpdKeys.StorageFreeSpace, WpdKeys.StorageAccessCapability);
        var roots = new List<StoreRoot>();
        foreach (var id in Children(WpdGuids.DeviceObjectId, ct))
        {
            _properties.GetValues(id, keys, out var values);
            if (GuidOf(values, WpdKeys.ObjectContentType) != WpdGuids.ContentFunctionalObject
                || GuidOf(values, WpdKeys.FunctionalObjectCategory) != WpdGuids.CategoryStorage)
                continue;
            roots.Add(new StoreRoot(id,
                TextOf(values, WpdKeys.StorageDescription) ?? TextOf(values, WpdKeys.ObjectName) ?? id,
                TextOf(values, WpdKeys.StorageFileSystemType),
                LongOf(values, WpdKeys.StorageCapacity), LongOf(values, WpdKeys.StorageFreeSpace),
                IsReadOnly || UIntOf(values, WpdKeys.StorageAccessCapability) is > 0));
        }
        return roots;
    }, ct);

    public Task<IReadOnlyList<StoreEntry>> ListAsync(string folderId, CancellationToken ct) => Task.Run<IReadOnlyList<StoreEntry>>(() =>
    {
        var entries = new List<StoreEntry>();
        foreach (var id in Children(folderId, ct))
        {
            _properties.GetValues(id, _listKeys, out var values);
            if (BoolOf(values, WpdKeys.ObjectIsHidden) && BoolOf(values, WpdKeys.ObjectIsSystem)) continue;
            entries.Add(EntryOf(id, values));
        }
        return entries;
    }, ct);

    public Task<Stream> OpenReadAsync(string fileId, CancellationToken ct) => Task.Run<Stream>(() =>
    {
        uint optimal = BufferSize;
        var key = WpdKeys.ResourceDefault;
        _resources.GetStream(fileId, ref key, 0 /* STGM_READ */, ref optimal, out var stream);
        return new ComReadStream(stream);
    }, ct);

    public Task<StoreEntry> WriteAsync(string folderId, string name, long length, DateTime? modified, Stream content, CancellationToken ct) => Task.Run(async () =>
    {
        var values = NewObject(folderId, name, WpdGuids.ContentGenericFile, WpdGuids.FormatUnspecified);
        var size = WpdKeys.ObjectSize;
        values.SetUnsignedLargeIntegerValue(ref size, (ulong)length);
        if (modified is { } when)
        {
            var date = PropVariant.OleDate(when);
            var key = WpdKeys.ObjectDateModified;
            values.SetValue(ref key, ref date);
        }

        uint optimal = BufferSize;
        IntPtr cookie = IntPtr.Zero;
        _content.CreateObjectWithPropertiesAndData(values, out var data, ref optimal, ref cookie);
        if (cookie != IntPtr.Zero) Marshal.FreeCoTaskMem(cookie);
        try
        {
            var buffer = new byte[Math.Max(optimal, 64 * 1024)];
            int read;
            while ((read = await content.ReadAsync(buffer, ct)) > 0) data.Write(buffer, read, IntPtr.Zero);
            data.Commit(0);
            ((IPortableDeviceDataStream)data).GetObjectID(out var id);
            return new StoreEntry(id, name, IsFolder: false, length, modified);
        }
        catch
        {
            ((IPortableDeviceDataStream)data).Cancel();
            throw;
        }
        finally
        {
            Marshal.ReleaseComObject(data);
        }
    }, ct);

    public Task<StoreEntry> CreateFolderAsync(string folderId, string name, CancellationToken ct) => Task.Run(() =>
    {
        var values = NewObject(folderId, name, WpdGuids.ContentFolder, WpdGuids.FormatPropertiesOnly);
        IntPtr made = IntPtr.Zero;
        _content.CreateObjectWithPropertiesOnly(values, ref made);
        try { return new StoreEntry(Marshal.PtrToStringUni(made)!, name, IsFolder: true, 0, DateTime.Now); }
        finally { Marshal.FreeCoTaskMem(made); }
    }, ct);

    public Task<StoreEntry> RenameAsync(StoreEntry entry, string name, CancellationToken ct) => Task.Run(() =>
    {
        // The file name is what a phone shows and keeps; the object name follows it where the device lets it.
        var values = WpdGuids.Create<IPortableDeviceValues>(WpdGuids.PortableDeviceValues);
        var original = WpdKeys.ObjectOriginalFileName;
        values.SetStringValue(ref original, name);
        _properties.SetValues(entry.Id, values, out var results);
        var error = WpdKeys.ObjectOriginalFileName;
        try { results.GetErrorValue(ref error, out var hr); Marshal.ThrowExceptionForHR(hr); }
        catch (COMException e) when (e.HResult == unchecked((int)0x80070490)) { } // ERROR_NOT_FOUND: no error recorded for the key
        return entry with { Name = name };
    }, ct);

    public Task DeleteAsync(StoreEntry entry, CancellationToken ct) => Task.Run(() =>
    {
        var ids = WpdGuids.Create<IPortableDevicePropVariantCollection>(WpdGuids.PortableDevicePropVariantCollection);
        var id = PropVariant.Text(entry.Id);
        try { ids.Add(ref id); }
        finally { PropVariant.PropVariantClear(ref id); }
        _content.Delete(DeleteWithRecursion, ids, IntPtr.Zero);
    }, ct);

    public string? LocalPathOf(string id) => null;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            if (_cookie is not null) _device.Unadvise(_cookie);
            _device.Close();
        }
        catch (COMException) { }
        Marshal.ReleaseComObject(_device);
    }

    // ── Reading the device ─────────────────────────────────────────────────────────────────────────────────

    IEnumerable<string> Children(string parentId, CancellationToken ct)
    {
        _content.EnumObjects(0, parentId, null, out var objects);
        var batch = new IntPtr[EnumerateBatch];
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            uint fetched = 0;
            int hr = objects.Next(EnumerateBatch, batch, ref fetched);
            for (int i = 0; i < fetched; i++)
            {
                string id = Marshal.PtrToStringUni(batch[i])!;
                Marshal.FreeCoTaskMem(batch[i]);
                yield return id;
            }
            if (hr != 0 || fetched == 0) yield break;   // S_FALSE: the last of them
        }
    }

    string DeviceName()
    {
        try
        {
            _properties.GetValues(WpdGuids.DeviceObjectId, Keys(WpdKeys.DeviceFriendlyName, WpdKeys.DeviceModel), out var values);
            return TextOf(values, WpdKeys.DeviceFriendlyName) ?? TextOf(values, WpdKeys.DeviceModel) ?? "Portable device";
        }
        catch (COMException) { return "Portable device"; }
    }

    /// <summary>The device object's own account of itself, each property it answers: "protocol" is the MTP version it
    /// speaks ("MTP: 1.10"), "type" the WPD_DEVICE_TYPES value (0 generic, 1 camera, 2 media player, 3 phone …).</summary>
    Dictionary<string, string> DeviceFacts()
    {
        var facts = new Dictionary<string, string>();
        try
        {
            _properties.GetValues(WpdGuids.DeviceObjectId,
                Keys(WpdKeys.DeviceManufacturer, WpdKeys.DeviceModel, WpdKeys.DeviceFirmwareVersion, WpdKeys.DeviceProtocol, WpdKeys.DeviceType),
                out var values);
            foreach (var (name, key) in new[] { ("manufacturer", WpdKeys.DeviceManufacturer), ("model", WpdKeys.DeviceModel),
                         ("firmware", WpdKeys.DeviceFirmwareVersion), ("protocol", WpdKeys.DeviceProtocol) })
                if (TextOf(values, key) is { } text) facts[name] = text;
            if (UIntOf(values, WpdKeys.DeviceType) is { } type) facts["type"] = type.ToString();
        }
        catch (COMException e) { facts["error"] = $"0x{e.HResult:X8} {e.Message}"; }
        return facts;
    }

    static StoreEntry EntryOf(string id, IPortableDeviceValues values)
    {
        var type = GuidOf(values, WpdKeys.ObjectContentType);
        bool folder = type == WpdGuids.ContentFolder || type == WpdGuids.ContentFunctionalObject;
        return new StoreEntry(id,
            TextOf(values, WpdKeys.ObjectOriginalFileName) ?? TextOf(values, WpdKeys.ObjectName) ?? id,
            folder, folder ? 0 : LongOf(values, WpdKeys.ObjectSize) ?? 0, DateOf(values, WpdKeys.ObjectDateModified));
    }

    IPortableDeviceValues NewObject(string parentId, string name, Guid contentType, Guid format)
    {
        var values = WpdGuids.Create<IPortableDeviceValues>(WpdGuids.PortableDeviceValues);
        var parent = WpdKeys.ObjectParentId;
        var objectName = WpdKeys.ObjectName;
        var original = WpdKeys.ObjectOriginalFileName;
        var type = WpdKeys.ObjectContentType;
        var formatKey = WpdKeys.ObjectFormat;
        values.SetStringValue(ref parent, parentId);
        values.SetStringValue(ref objectName, name);
        values.SetStringValue(ref original, name);
        values.SetGuidValue(ref type, ref contentType);
        values.SetGuidValue(ref formatKey, ref format);
        return values;
    }

    static IPortableDeviceValues ClientInfo(uint access)
    {
        var info = WpdGuids.Create<IPortableDeviceValues>(WpdGuids.PortableDeviceValues);
        var version = typeof(WpdStore).Assembly.GetName().Version ?? new Version(1, 0, 0);
        var name = WpdKeys.ClientName;
        var major = WpdKeys.ClientMajorVersion;
        var minor = WpdKeys.ClientMinorVersion;
        var revision = WpdKeys.ClientRevision;
        var quality = WpdKeys.ClientSecurityQualityOfService;
        var desired = WpdKeys.ClientDesiredAccess;
        info.SetStringValue(ref name, "Nexaflow");
        info.SetUnsignedIntegerValue(ref major, (uint)version.Major);
        info.SetUnsignedIntegerValue(ref minor, (uint)version.Minor);
        info.SetUnsignedIntegerValue(ref revision, (uint)Math.Max(0, version.Build));
        info.SetUnsignedIntegerValue(ref quality, SecurityImpersonation);
        info.SetUnsignedIntegerValue(ref desired, access);
        return info;
    }

    static IPortableDeviceKeyCollection Keys(params PropertyKey[] keys)
    {
        var collection = WpdGuids.Create<IPortableDeviceKeyCollection>(WpdGuids.PortableDeviceKeyCollection);
        for (int i = 0; i < keys.Length; i++) collection.Add(ref keys[i]);
        return collection;
    }

    // A value the device did not give is absent, which the typed getters report as an error: read as null.

    static string? TextOf(IPortableDeviceValues values, PropertyKey key)
    {
        try { values.GetStringValue(ref key, out var text); return string.IsNullOrEmpty(text) ? null : text; }
        catch (COMException) { return null; }
    }

    static Guid? GuidOf(IPortableDeviceValues values, PropertyKey key)
    {
        try { values.GetGuidValue(ref key, out var guid); return guid; }
        catch (COMException) { return null; }
    }

    static long? LongOf(IPortableDeviceValues values, PropertyKey key)
    {
        try { values.GetUnsignedLargeIntegerValue(ref key, out var value); return (long)value; }
        catch (COMException) { return null; }
    }

    static uint? UIntOf(IPortableDeviceValues values, PropertyKey key)
    {
        try { values.GetUnsignedIntegerValue(ref key, out var value); return value; }
        catch (COMException) { return null; }
    }

    static bool BoolOf(IPortableDeviceValues values, PropertyKey key)
    {
        try { values.GetBoolValue(ref key, out var value); return value; }
        catch (COMException) { return false; }
    }

    static DateTime? DateOf(IPortableDeviceValues values, PropertyKey key)
    {
        try
        {
            values.GetValue(ref key, out var value);
            try { return value.Type == PropVariant.Date ? DateTime.FromOADate(value.Double) : null; }
            finally { PropVariant.PropVariantClear(ref value); }
        }
        catch (COMException) { return null; }
    }

    /// <summary>The device's events: anything added, removed or changed on it — a storage shared on unlocking among
    /// them — is the store changing.</summary>
    [ComVisible(true)]
    sealed class Events(WpdStore store) : IPortableDeviceEventCallback
    {
        public void OnEvent(IPortableDeviceValues eventParameters)
        {
            if (!store._disposed) store.Changed?.Invoke(store, EventArgs.Empty);
        }
    }

    /// <summary>A WPD resource read as a .NET stream.</summary>
    sealed class ComReadStream(ComStream stream) : Stream
    {
        readonly IntPtr _read = Marshal.AllocCoTaskMem(sizeof(int));

        public override int Read(byte[] buffer, int offset, int count)
        {
            var chunk = offset == 0 ? buffer : new byte[count];
            stream.Read(chunk, count, _read);
            int read = Marshal.ReadInt32(_read);
            if (offset != 0) Array.Copy(chunk, 0, buffer, offset, read);
            return read;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            Marshal.FreeCoTaskMem(_read);
            Marshal.ReleaseComObject(stream);
            base.Dispose(disposing);
        }
    }
}
