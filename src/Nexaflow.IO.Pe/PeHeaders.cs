namespace Nexaflow.IO.Pe;

/// <summary>The 16 well-known data-directory slots of the optional header.</summary>
public enum PeDirectory
{
    Export = 0, Import = 1, Resource = 2, Exception = 3,
    /// <summary>Authenticode. Uniquely, its "VirtualAddress" is a <em>file offset</em>, not an RVA.</summary>
    Security = 4,
    BaseRelocation = 5, Debug = 6, Architecture = 7, GlobalPointer = 8,
    Tls = 9, LoadConfig = 10, BoundImport = 11, ImportAddressTable = 12,
    DelayImport = 13, ClrHeader = 14, Reserved = 15,
}

public enum PeMachine : ushort
{
    Unknown = 0, I386 = 0x14C, Thumb = 0x1C2, ArmNt = 0x1C4, Arm = 0x1C0,
    Ia64 = 0x200, Amd64 = 0x8664, Arm64 = 0xAA64, Arm64Ec = 0xA641, Arm64X = 0xA64E,
    Ebc = 0xEBC, RiscV32 = 0x5032, RiscV64 = 0x5064, LoongArch64 = 0x6264,
}

public enum PeSubsystem : ushort
{
    Unknown = 0, Native = 1, WindowsGui = 2, WindowsCui = 3, Os2Cui = 5, PosixCui = 7,
    NativeWindows = 8, WindowsCeGui = 9, EfiApplication = 10, EfiBootServiceDriver = 11,
    EfiRuntimeDriver = 12, EfiRom = 13, Xbox = 14, WindowsBootApplication = 16,
}

[Flags]
public enum PeFileCharacteristics : ushort
{
    None = 0, RelocsStripped = 0x0001, ExecutableImage = 0x0002, LineNumsStripped = 0x0004,
    LocalSymsStripped = 0x0008, AggressiveWsTrim = 0x0010, LargeAddressAware = 0x0020,
    BytesReversedLo = 0x0080, Machine32Bit = 0x0100, DebugStripped = 0x0200,
    RemovableRunFromSwap = 0x0400, NetRunFromSwap = 0x0800, System = 0x1000, Dll = 0x2000,
    UpSystemOnly = 0x4000, BytesReversedHi = 0x8000,
}

[Flags]
public enum PeDllCharacteristics : ushort
{
    None = 0, HighEntropyVa = 0x0020, DynamicBase = 0x0040, ForceIntegrity = 0x0080,
    NxCompat = 0x0100, NoIsolation = 0x0200, NoSeh = 0x0400, NoBind = 0x0800,
    AppContainer = 0x1000, WdmDriver = 0x2000, GuardCf = 0x4000, TerminalServerAware = 0x8000,
}

/// <summary>The MS-DOS stub header. Only <see cref="NtHeaderOffset"/> matters to a modern loader.</summary>
public sealed record PeDosHeader(ushort Magic, uint NtHeaderOffset, byte[] StubBytes)
{
    /// <summary>"MZ".</summary>
    public bool IsValid => Magic == 0x5A4D;

    /// <summary>True when the stub is not the linker's stock "This program cannot be run in DOS mode"
    /// — worth noticing, since packers and installers often hide data here.</summary>
    public bool HasCustomStub { get; init; }
}

public sealed record PeCoffHeader(
    PeMachine             Machine,
    ushort                NumberOfSections,
    uint                  TimeDateStamp,
    uint                  PointerToSymbolTable,
    uint                  NumberOfSymbols,
    ushort                SizeOfOptionalHeader,
    PeFileCharacteristics Characteristics)
{
    /// <summary>The link timestamp, or null when it is zero or a deterministic-build content hash
    /// (which is not a time at all — see <see cref="PeDebug.IsDeterministic"/>).</summary>
    public DateTimeOffset? Timestamp
        => TimeDateStamp is 0 or 0xFFFFFFFF ? null : DateTimeOffset.FromUnixTimeSeconds(TimeDateStamp);
}

public sealed record PeDataDirectory(PeDirectory Kind, uint VirtualAddress, uint Size)
{
    public bool IsPresent => VirtualAddress != 0 && Size != 0;
    public override string ToString() => $"{Kind}: RVA 0x{VirtualAddress:X8} size {Size}";
}

/// <summary>
/// The optional header, unified across PE32 and PE32+. The size-typed fields (<see cref="ImageBase"/>,
/// stack/heap reserves) are 32-bit in PE32 and 64-bit in PE32+; both are widened to
/// <see cref="ulong"/> here so callers never branch on bitness to read a value.
/// </summary>
public sealed record PeOptionalHeader(
    ushort               Magic,
    byte                 MajorLinkerVersion,
    byte                 MinorLinkerVersion,
    uint                 SizeOfCode,
    uint                 SizeOfInitializedData,
    uint                 SizeOfUninitializedData,
    uint                 AddressOfEntryPoint,
    uint                 BaseOfCode,
    uint                 BaseOfData,          // PE32 only; 0 for PE32+
    ulong                ImageBase,
    uint                 SectionAlignment,
    uint                 FileAlignment,
    ushort               MajorOperatingSystemVersion,
    ushort               MinorOperatingSystemVersion,
    ushort               MajorImageVersion,
    ushort               MinorImageVersion,
    ushort               MajorSubsystemVersion,
    ushort               MinorSubsystemVersion,
    uint                 SizeOfImage,
    uint                 SizeOfHeaders,
    uint                 CheckSum,
    PeSubsystem          Subsystem,
    PeDllCharacteristics DllCharacteristics,
    ulong                SizeOfStackReserve,
    ulong                SizeOfStackCommit,
    ulong                SizeOfHeapReserve,
    ulong                SizeOfHeapCommit,
    uint                 NumberOfRvaAndSizes)
{
    public const ushort Pe32Magic     = 0x10B;
    public const ushort Pe32PlusMagic = 0x20B;
    public const ushort RomMagic      = 0x107;

    public bool Is64Bit => Magic == Pe32PlusMagic;

    public string LinkerVersion => $"{MajorLinkerVersion}.{MinorLinkerVersion}";
    public string SubsystemVersion => $"{MajorSubsystemVersion}.{MinorSubsystemVersion}";
}
