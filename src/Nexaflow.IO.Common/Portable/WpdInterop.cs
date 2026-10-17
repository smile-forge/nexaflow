using System.Runtime.InteropServices;
using ComStream = System.Runtime.InteropServices.ComTypes.IStream;

namespace Nexaflow.IO.Common.Portable;

// Windows Portable Devices (PortableDeviceApi.dll), called directly. Every interface lists its methods in the order
// PortableDeviceApi.idl / portabledevicetypes.idl (Windows SDK 10.0.26100) declares them, since that order is the
// vtable; a method this code never calls keeps its slot as a parameterless placeholder.

[StructLayout(LayoutKind.Sequential)]
internal struct PropertyKey(Guid formatId, uint propertyId)
{
    public Guid FormatId = formatId;
    public uint PropertyId = propertyId;
}

/// <summary>A PROPVARIANT, as far as this code reads and writes one: its type and the eight bytes after the header.</summary>
[StructLayout(LayoutKind.Explicit, Size = 24)]
internal struct PropVariant
{
    public const ushort LpWStr = 31, Date = 7;

    [FieldOffset(0)] public ushort Type;
    [FieldOffset(8)] public IntPtr Pointer;
    [FieldOffset(8)] public double Double;

    public static PropVariant Text(string value) => new() { Type = LpWStr, Pointer = Marshal.StringToCoTaskMemUni(value) };

    public static PropVariant OleDate(DateTime value) => new() { Type = Date, Double = value.ToOADate() };

    [DllImport("ole32.dll")]
    public static extern int PropVariantClear(ref PropVariant value);
}

internal static class WpdKeys
{
    static readonly Guid Object = new("EF6B490D-5CD8-437A-AFFC-DA8B60EE4A3C");
    static readonly Guid Storage = new("01A3057A-74D6-4E80-BEA7-DC4C212CE50A");
    static readonly Guid Client = new("204D9F0C-2292-4080-9F42-40664E70F859");
    static readonly Guid Device = new("26D4979A-E643-4626-9E2B-736DC0C92FDC");

    public static PropertyKey ObjectParentId = new(Object, 3);
    public static PropertyKey ObjectName = new(Object, 4);
    public static PropertyKey ObjectFormat = new(Object, 6);
    public static PropertyKey ObjectContentType = new(Object, 7);
    public static PropertyKey ObjectIsHidden = new(Object, 9);
    public static PropertyKey ObjectIsSystem = new(Object, 10);
    public static PropertyKey ObjectSize = new(Object, 11);
    public static PropertyKey ObjectOriginalFileName = new(Object, 12);
    public static PropertyKey ObjectDateModified = new(Object, 19);

    public static PropertyKey FunctionalObjectCategory = new(new Guid("8F052D93-ABCA-4FC5-A5AC-B01DF4DBE598"), 2);

    public static PropertyKey StorageFileSystemType = new(Storage, 3);
    public static PropertyKey StorageCapacity = new(Storage, 4);
    public static PropertyKey StorageFreeSpace = new(Storage, 5);
    public static PropertyKey StorageDescription = new(Storage, 7);
    public static PropertyKey StorageAccessCapability = new(Storage, 11);

    public static PropertyKey ClientName = new(Client, 2);
    public static PropertyKey ClientMajorVersion = new(Client, 3);
    public static PropertyKey ClientMinorVersion = new(Client, 4);
    public static PropertyKey ClientRevision = new(Client, 5);
    public static PropertyKey ClientSecurityQualityOfService = new(Client, 8);
    public static PropertyKey ClientDesiredAccess = new(Client, 9);

    public static PropertyKey DeviceFriendlyName = new(Device, 12);
    public static PropertyKey DeviceModel = new(Device, 8);

    public static PropertyKey DeviceFirmwareVersion = new(Device, 3);
    public static PropertyKey DeviceProtocol = new(Device, 6);
    public static PropertyKey DeviceManufacturer = new(Device, 7);
    public static PropertyKey DeviceType = new(Device, 15);

    public static PropertyKey ResourceDefault = new(new Guid("E81E79BE-34F0-41BF-B53F-F1A06AE87842"), 0);
}

internal static class WpdGuids
{
    public static readonly Guid DeviceInterface = new("6AC27878-A6FA-4155-BA85-F98F491D4F33");
    public static readonly Guid ContentFunctionalObject = new("99ED0160-17FF-4C44-9D98-1D7A6F941921");
    public static readonly Guid ContentFolder = new("27E2E392-A111-48E0-AB0C-E17705A05F85");
    public static readonly Guid ContentGenericFile = new("0085E0A6-8D34-45D7-BC5C-447E59C73D48");
    public static readonly Guid CategoryStorage = new("23F05BBC-15DE-4C2A-A55B-A9AF5CE412EF");
    public static readonly Guid FormatUnspecified = new("30000000-AE6C-4804-98BA-C57B46965FE7");
    public static readonly Guid FormatPropertiesOnly = new("30010000-AE6C-4804-98BA-C57B46965FE7");

    public static readonly Guid PortableDeviceFtm = new("F7C0039A-4762-488A-B4B3-760EF9A1BA9B");
    public static readonly Guid PortableDeviceValues = new("0C15D503-D017-47CE-9016-7B3F978721CC");
    public static readonly Guid PortableDeviceKeyCollection = new("DE2D022D-2480-43BE-97F0-D1FA2CF98F4F");
    public static readonly Guid PortableDevicePropVariantCollection = new("08A99E2F-6D6D-4B80-AF5A-BAF2BCBE4CB9");

    public const string DeviceObjectId = "DEVICE";

    public static T Create<T>(Guid classId) => (T)Activator.CreateInstance(Type.GetTypeFromCLSID(classId, throwOnError: true)!)!;
}

[ComImport, Guid("625E2DF8-6392-4CF0-9AD1-3CFA5F17775C"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPortableDevice
{
    void Open([MarshalAs(UnmanagedType.LPWStr)] string pnpDeviceId, IPortableDeviceValues clientInfo);
    void SendCommand();
    void Content(out IPortableDeviceContent content);
    void Capabilities();
    void Cancel();
    void Close();
    void Advise(uint flags, IPortableDeviceEventCallback callback, IPortableDeviceValues? parameters, [MarshalAs(UnmanagedType.LPWStr)] out string cookie);
    void Unadvise([MarshalAs(UnmanagedType.LPWStr)] string cookie);
    void GetPnPDeviceID();
}

[ComImport, Guid("6A96ED84-7C73-4480-9938-BF5AF477D426"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPortableDeviceContent
{
    void EnumObjects(uint flags, [MarshalAs(UnmanagedType.LPWStr)] string parentObjectId, IPortableDeviceValues? filter, out IEnumPortableDeviceObjectIDs objects);
    void Properties(out IPortableDeviceProperties properties);
    void Transfer(out IPortableDeviceResources resources);
    void CreateObjectWithPropertiesOnly(IPortableDeviceValues values, ref IntPtr objectId);
    void CreateObjectWithPropertiesAndData(IPortableDeviceValues values, out ComStream data, ref uint optimalWriteBufferSize, ref IntPtr cookie);
    void Delete(uint options, IPortableDevicePropVariantCollection objectIds, IntPtr results);
    void GetObjectIDsFromPersistentUniqueIDs();
    void Cancel();
    void Move();
    void Copy();
}

[ComImport, Guid("10ECE955-CF41-4728-BFA0-41EEDF1BBF19"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IEnumPortableDeviceObjectIDs
{
    [PreserveSig] int Next(uint count, [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] IntPtr[] objectIds, ref uint fetched);
    void Skip();
    void Reset();
    void Clone();
    void Cancel();
}

[ComImport, Guid("7F6D695C-03DF-4439-A809-59266BEEE3A6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPortableDeviceProperties
{
    void GetSupportedProperties();
    void GetPropertyAttributes();
    void GetValues([MarshalAs(UnmanagedType.LPWStr)] string objectId, IPortableDeviceKeyCollection? keys, out IPortableDeviceValues values);
    void SetValues([MarshalAs(UnmanagedType.LPWStr)] string objectId, IPortableDeviceValues values, out IPortableDeviceValues results);
    void Delete();
    void Cancel();
}

[ComImport, Guid("FD8878AC-D841-4D17-891C-E6829CDB6934"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPortableDeviceResources
{
    void GetSupportedResources();
    void GetResourceAttributes();
    void GetStream([MarshalAs(UnmanagedType.LPWStr)] string objectId, ref PropertyKey key, uint mode, ref uint optimalBufferSize, out ComStream stream);
    void Delete();
    void Cancel();
    void CreateResource();
}

/// <summary>The stream a new object's data is written through: an IStream, then GetObjectID once committed.</summary>
[ComImport, Guid("88E04DB3-1012-4D64-9996-F703A950D3F4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPortableDeviceDataStream
{
    // IStream's eleven, in its order: Read, Write, Seek, SetSize, CopyTo, Commit, Revert, LockRegion, UnlockRegion, Stat, Clone.
    void Read(); void Write(); void Seek(); void SetSize(); void CopyTo(); void Commit(); void Revert();
    void LockRegion(); void UnlockRegion(); void Stat(); void Clone();
    void GetObjectID([MarshalAs(UnmanagedType.LPWStr)] out string objectId);
    void Cancel();
}

[ComImport, Guid("A8792A31-F385-493C-A893-40F64EB45F6E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPortableDeviceEventCallback
{
    void OnEvent(IPortableDeviceValues eventParameters);
}

[ComImport, Guid("6848F6F2-3155-4F86-B6F5-263EEEAB3143"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPortableDeviceValues
{
    void GetCount(out uint count);
    void GetAt();
    void SetValue(ref PropertyKey key, ref PropVariant value);
    void GetValue(ref PropertyKey key, out PropVariant value);
    void SetStringValue(ref PropertyKey key, [MarshalAs(UnmanagedType.LPWStr)] string value);
    void GetStringValue(ref PropertyKey key, [MarshalAs(UnmanagedType.LPWStr)] out string value);
    void SetUnsignedIntegerValue(ref PropertyKey key, uint value);
    void GetUnsignedIntegerValue(ref PropertyKey key, out uint value);
    void SetSignedIntegerValue();
    void GetSignedIntegerValue();
    void SetUnsignedLargeIntegerValue(ref PropertyKey key, ulong value);
    void GetUnsignedLargeIntegerValue(ref PropertyKey key, out ulong value);
    void SetSignedLargeIntegerValue();
    void GetSignedLargeIntegerValue();
    void SetFloatValue();
    void GetFloatValue();
    void SetErrorValue();
    void GetErrorValue(ref PropertyKey key, out int value);
    void SetKeyValue();
    void GetKeyValue();
    void SetBoolValue();
    void GetBoolValue(ref PropertyKey key, [MarshalAs(UnmanagedType.Bool)] out bool value);
    void SetIUnknownValue();
    void GetIUnknownValue();
    void SetGuidValue(ref PropertyKey key, ref Guid value);
    void GetGuidValue(ref PropertyKey key, out Guid value);
    void SetBufferValue();
    void GetBufferValue();
    void SetIPortableDeviceValuesValue();
    void GetIPortableDeviceValuesValue();
    void SetIPortableDevicePropVariantCollectionValue();
    void GetIPortableDevicePropVariantCollectionValue();
    void SetIPortableDeviceKeyCollectionValue();
    void GetIPortableDeviceKeyCollectionValue();
    void SetIPortableDeviceValuesCollectionValue();
    void GetIPortableDeviceValuesCollectionValue();
    void RemoveValue();
    void CopyValuesFromPropertyStore();
    void CopyValuesToPropertyStore();
    void Clear();
}

[ComImport, Guid("DADA2357-E0AD-492E-98DB-DD61C53BA353"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPortableDeviceKeyCollection
{
    void GetCount();
    void GetAt();
    void Add(ref PropertyKey key);
    void Clear();
    void RemoveAt();
}

[ComImport, Guid("89B2E422-4F1B-4316-BCEF-A44AFEA83EB3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPortableDevicePropVariantCollection
{
    void GetCount();
    void GetAt();
    void Add(ref PropVariant value);
    void GetVariantType();
    void ChangeType();
    void Clear();
    void RemoveAt();
}
