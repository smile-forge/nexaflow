using System.Runtime.InteropServices;

namespace Nexaflow.Features.WindowsSearch.Services;

// ─────────────────────────────────────────────────────────────────────────────────────────────────
// Windows' structured-query parser, transcribed from the SDK's structuredquery.h and
// structuredquerycondition.h. Read the header note in SearchApiInterop.cs first — the same rules
// apply, and one extra trap lives here:
//
//   ICondition derives from IPersistStream, NOT IUnknown. Its own methods start at vtable slot 8,
//   behind GetClassID/IsDirty/Load/Save/GetSizeMax. Declaring GetConditionType first would put it on
//   IPersist::GetClassID — an access violation, not an exception. Likewise IQuerySolution derives
//   from IConditionFactory, so MakeNot/MakeAndOr/MakeLeaf/Resolve precede GetQuery.
//
// This is the API that gives us a condition TREE rather than a SQL string: one parse of what the user
// typed, projected into both the index query and the folder-walk predicate, so the two can never
// disagree about what the query meant.
// ─────────────────────────────────────────────────────────────────────────────────────────────────

internal enum ConditionType
{
    And = 0,
    Or = 1,
    Not = 2,
    Leaf = 3,
}

internal enum ConditionOperation
{
    Implicit = 0,
    Equal = 1,
    NotEqual = 2,
    LessThan = 3,
    GreaterThan = 4,
    LessThanOrEqual = 5,
    GreaterThanOrEqual = 6,
    ValueStartsWith = 7,
    ValueEndsWith = 8,
    ValueContains = 9,
    ValueNotContains = 10,
    DosWildcards = 11,
    WordEqual = 12,
    WordStartsWith = 13,
    ApplicationSpecific = 14,
}

/// <summary>
/// A PROPVARIANT, sized so it occupies the right stack slot. The payload is never read field by field
/// here — the propsys helpers in <see cref="PropVariantReader"/> do the type coercion, because doing it
/// by hand means re-implementing every VT_ case Windows already handles.
/// </summary>
/// <summary>
/// A Win32 SYSTEMTIME. Resolve declares its reference time <c>[ref]</c>, so it must be a real pointer —
/// passing null there is what makes the whole resolve fail, taking every typed value with it.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct SystemTime
{
    public ushort Year, Month, DayOfWeek, Day, Hour, Minute, Second, Milliseconds;

    public static SystemTime Now()
    {
        var t = DateTime.Now;
        return new SystemTime
        {
            Year = (ushort)t.Year,
            Month = (ushort)t.Month,
            DayOfWeek = (ushort)t.DayOfWeek,
            Day = (ushort)t.Day,
            Hour = (ushort)t.Hour,
            Minute = (ushort)t.Minute,
            Second = (ushort)t.Second,
            Milliseconds = (ushort)t.Millisecond,
        };
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct PropVariant
{
    public ushort VarType;
    private readonly ushort _reserved1;
    private readonly ushort _reserved2;
    private readonly ushort _reserved3;
    private readonly IntPtr _value1;
    private readonly IntPtr _value2;
}

[ComImport]
[Guid("00000100-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IEnumUnknown
{
    [PreserveSig]
    int Next(uint celt,
             [Out, MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.IUnknown, SizeParamIndex = 0)]
             object?[] rgelt,
             out uint pceltFetched);

    [PreserveSig] int Skip(uint celt);
    void Reset();
    void Clone(out IEnumUnknown ppenum);
}

/// <summary>One node of a parsed query: an AND/OR/NOT branch, or a leaf naming a property, an
/// operation and a value.</summary>
[ComImport]
[Guid("0FC988D4-C935-4B97-A973-46282EA175C8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ICondition
{
    // Slots 3-7 — IPersist + IPersistStream. Position only, never called.
    void GetClassID();
    void IsDirty();
    void Load();
    void Save();
    void GetSizeMax();

    /// <summary>Slot 8.</summary>
    ConditionType GetConditionType();

    /// <summary>Slot 9. The children of an AND/OR/NOT node, as an IEnumUnknown of ICondition.</summary>
    [return: MarshalAs(UnmanagedType.Interface)]
    object GetSubConditions([In] ref Guid riid);

    /// <summary>Slot 10. A leaf's (property, operation, value). Any of the three may be omitted by the
    /// caller; we always want all three.</summary>
    [PreserveSig]
    int GetComparisonInfo(
        [MarshalAs(UnmanagedType.LPWStr)] out string? ppszPropertyName,
        out ConditionOperation pcop,
        out PropVariant ppropvar);

    // GetValueType, GetValueNormalization, GetInputTerms, Clone follow; not declared.
}

[ComImport]
[Guid("D6EBC66B-8921-4193-AFDD-A1789FB7FF57")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IQuerySolution
{
    // Slots 3-5 — inherited from IConditionFactory. Position only, never called.
    void MakeNot();
    void MakeAndOr();
    void MakeLeaf();

    /// <summary>
    /// Slot 6, inherited from IConditionFactory. Turns the raw parse into a resolved tree: relative dates
    /// ("last week") become real intervals and values become properly typed.
    /// <para>
    /// Not optional. Straight out of Parse, a leaf's value is an internal, untyped form — a size comes
    /// back as unreadable text rather than a number — so anything reading values must resolve first.
    /// </para>
    /// </summary>
    [PreserveSig]
    int Resolve(ICondition pc, int sqro, [In] ref SystemTime pstReferenceTime, out ICondition? ppcResolved);

    /// <summary>Slot 7. The parsed tree. The out IEntity is released by the caller.</summary>
    [PreserveSig]
    int GetQuery(out ICondition? ppQueryNode, out IntPtr ppMainType);

    // GetErrors, GetLexicalData follow; not declared.
}

[ComImport]
[Guid("2EBDEE67-3505-43F8-9946-EA44ABC8E5B0")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IQueryParser
{
    /// <summary>Slot 3.</summary>
    [return: MarshalAs(UnmanagedType.Interface)]
    IQuerySolution Parse([MarshalAs(UnmanagedType.LPWStr)] string pszInputString, IEnumUnknown? pCustomProperties);

    // SetOption, GetOption, SetMultiOption, GetSchemaProvider, RestateToString, ParsePropertyValue,
    // RestatePropertyValueToString follow; not declared.
}

[ComImport]
[Guid("A879E3C4-AF77-44FB-8F37-EBD1487CF920")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IQueryParserManager
{
    /// <summary>Slot 3. Builds a parser already loaded with a catalog's schema and localised keywords.</summary>
    [PreserveSig]
    int CreateLoadedParser(
        [MarshalAs(UnmanagedType.LPWStr)] string pszCatalog,
        ushort langidForKeywords,
        [In] ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out object? ppQueryParser);

    /// <summary>Slot 4. Must be called after CreateLoadedParser, or the parser has no keyword handling.</summary>
    [PreserveSig]
    int InitializeOptions(
        [MarshalAs(UnmanagedType.Bool)] bool fUnderstandNQS,
        [MarshalAs(UnmanagedType.Bool)] bool fAutoWildCard,
        IQueryParser pQueryParser);

    // SetOption follows; not declared.
}

/// <summary>Co-creation for <c>QueryParserManager</c>.</summary>
[ComImport]
[Guid("5088B39A-29B4-4D9D-8245-4EE289222F66")]
internal class QueryParserManager;

/// <summary>
/// Reads a PROPVARIANT into a CLR value. The VT space is large and full of vector/array variants, so the
/// propsys coercion helpers do the work — hand-decoding would mean re-implementing them, badly.
/// </summary>
internal static class PropVariantReader
{
    private const ushort VT_FILETIME = 64;
    private const ushort VT_DATE     = 7;
    private const ushort VT_BOOL     = 11;
    private const ushort VT_TYPEMASK = 0x0FFF;   // strips VT_VECTOR / VT_ARRAY / VT_BYREF

    // DllImport rather than LibraryImport: the source generator emits unsafe code, and this feature has
    // no other reason to enable it. Five small entry points don't earn that.
    [DllImport("propsys.dll")]
    private static extern int PropVariantToInt64(ref PropVariant pv, out long value);

    [DllImport("propsys.dll")]
    private static extern int PropVariantToBoolean(ref PropVariant pv, [MarshalAs(UnmanagedType.Bool)] out bool value);

    [DllImport("propsys.dll")]
    private static extern int PropVariantToFileTime(ref PropVariant pv, int pstfFlags, out long pftOut);

    [DllImport("propsys.dll")]
    private static extern int PropVariantToStringAlloc(ref PropVariant pv, out IntPtr ppszOut);

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant pv);

    /// <summary>
    /// The value as a long, a DateTime, a bool or a string — or null when it can't be read at all.
    /// <para>
    /// The variant type decides, never the order of attempts. Every coercion here succeeds on more than
    /// it should: a VT_FILETIME reads perfectly well as an integer (a meaningless tick count), and a
    /// VT_UI8 reads perfectly well as a string ("1048576"), which the SQL emitter would then quote and
    /// compare as text — <c>System.Size &gt; '1048576'</c> is not the query the user asked for.
    /// </para>
    /// </summary>
    public static object? Read(ref PropVariant pv)
    {
        var vt = (ushort)(pv.VarType & VT_TYPEMASK);

        if (vt is VT_FILETIME or VT_DATE)
            return PropVariantToFileTime(ref pv, 0, out var ft) == 0
                ? DateTime.FromFileTimeUtc(ft)
                : null;

        if (vt == VT_BOOL)
            return PropVariantToBoolean(ref pv, out var b) == 0 ? b : null;

        if (IsIntegral(vt))
            return PropVariantToInt64(ref pv, out var n) == 0 ? n : null;

        if (PropVariantToStringAlloc(ref pv, out var str) == 0 && str != IntPtr.Zero)
        {
            try { return Marshal.PtrToStringUni(str); }
            finally { Marshal.FreeCoTaskMem(str); }
        }

        return null;
    }

    // VT_I2/I4, VT_I1/UI1/UI2/UI4/I8/UI8, VT_INT/UINT. VT_R4/R8 are deliberately absent — a real is not
    // a size or a count, and rounding one into a long would be a silent change of meaning.
    private static bool IsIntegral(ushort vt) =>
        vt is 2 or 3 or 16 or 17 or 18 or 19 or 20 or 21 or 22 or 23;

    public static void Clear(ref PropVariant pv) => PropVariantClear(ref pv);
}
