using Microsoft.Win32;
using Nexaflow.Features.WindowsRegistry.Services;

namespace Nexaflow.Tests.Features.WindowsRegistry;

/// <summary>
/// The pure value-conversion logic: type labels, display/edit formatting, the bridge "wire" form, and
/// user-input parsing. Fully hermetic — no registry access.
/// </summary>
[TestClass]
public class RegistryValueCodecTests
{
    [TestMethod]
    public void TypeLabel_MapsEachKind()
    {
        Assert.AreEqual("REG_SZ",        RegistryValueCodec.TypeLabel(RegistryValueKind.String));
        Assert.AreEqual("REG_EXPAND_SZ", RegistryValueCodec.TypeLabel(RegistryValueKind.ExpandString));
        Assert.AreEqual("REG_DWORD",     RegistryValueCodec.TypeLabel(RegistryValueKind.DWord));
        Assert.AreEqual("REG_QWORD",     RegistryValueCodec.TypeLabel(RegistryValueKind.QWord));
        Assert.AreEqual("REG_BINARY",    RegistryValueCodec.TypeLabel(RegistryValueKind.Binary));
        Assert.AreEqual("REG_MULTI_SZ",  RegistryValueCodec.TypeLabel(RegistryValueKind.MultiString));
        Assert.AreEqual("REG_NONE",      RegistryValueCodec.TypeLabel(RegistryValueKind.None));
        Assert.AreEqual("REG_SZ",        RegistryValueCodec.TypeLabel(RegistryValueKind.Unknown)); // fallback
    }

    [TestMethod]
    public void EditableKinds_AreTheSixWritableTypes()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                RegistryValueKind.String, RegistryValueKind.ExpandString, RegistryValueKind.DWord,
                RegistryValueKind.QWord, RegistryValueKind.Binary, RegistryValueKind.MultiString,
            },
            RegistryValueCodec.EditableKinds.ToArray());
        CollectionAssert.DoesNotContain(RegistryValueCodec.EditableKinds.ToArray(), RegistryValueKind.None);
    }

    // ── Display ──────────────────────────────────────────────────────────────

    [TestMethod]
    public void FormatForDisplay_Dword_ShowsHexAndDecimal()
    {
        Assert.AreEqual("0x0000001a (26)", RegistryValueCodec.FormatForDisplay(26, RegistryValueKind.DWord));
        Assert.AreEqual("0xffffffff (-1)", RegistryValueCodec.FormatForDisplay(-1, RegistryValueKind.DWord));
    }

    [TestMethod]
    public void FormatForDisplay_Qword_ShowsHexAndDecimal()
        => Assert.AreEqual("0x000000000000001a (26)", RegistryValueCodec.FormatForDisplay(26L, RegistryValueKind.QWord));

    [TestMethod]
    public void FormatForDisplay_Binary_IsSpaceSeparatedHex()
    {
        Assert.AreEqual("01 ab ff", RegistryValueCodec.FormatForDisplay(new byte[] { 0x01, 0xAB, 0xFF }, RegistryValueKind.Binary));
        Assert.AreEqual("", RegistryValueCodec.FormatForDisplay("not-bytes", RegistryValueKind.Binary));
    }

    [TestMethod]
    public void FormatForDisplay_MultiString_IsCommaSeparated()
        => Assert.AreEqual("a, b, c", RegistryValueCodec.FormatForDisplay(new[] { "a", "b", "c" }, RegistryValueKind.MultiString));

    [TestMethod]
    public void FormatForDisplay_StringAndNull()
    {
        Assert.AreEqual("hello", RegistryValueCodec.FormatForDisplay("hello", RegistryValueKind.String));
        Assert.AreEqual("", RegistryValueCodec.FormatForDisplay(null, RegistryValueKind.String));
    }

    // ── Wire round-trip (Encode ↔ Decode), the bridge contract ───────────────

    [TestMethod]
    public void Encode_Decode_Dword_RoundTrips()
    {
        var (data, kind) = RegistryValueCodec.Decode("DWord", RegistryValueCodec.Encode(26, RegistryValueKind.DWord));
        Assert.AreEqual(RegistryValueKind.DWord, kind);
        Assert.AreEqual(26, data);
    }

    [TestMethod]
    public void Encode_Decode_Binary_RoundTrips()
    {
        var bytes = new byte[] { 0x01, 0xAB, 0xFF };
        var (data, kind) = RegistryValueCodec.Decode("Binary", RegistryValueCodec.Encode(bytes, RegistryValueKind.Binary));
        Assert.AreEqual(RegistryValueKind.Binary, kind);
        CollectionAssert.AreEqual(bytes, (byte[])data);
    }

    [TestMethod]
    public void Encode_Decode_MultiString_RoundTrips()
    {
        var items = new[] { "alpha", "beta", "gamma" };
        var (data, kind) = RegistryValueCodec.Decode("MultiString", RegistryValueCodec.Encode(items, RegistryValueKind.MultiString));
        Assert.AreEqual(RegistryValueKind.MultiString, kind);
        CollectionAssert.AreEqual(items, (string[])data);
    }

    [TestMethod]
    public void Decode_UnknownType_FallsBackToString()
    {
        var (data, kind) = RegistryValueCodec.Decode("NoSuchType", "literal");
        Assert.AreEqual(RegistryValueKind.String, kind);
        Assert.AreEqual("literal", data);
    }

    [TestMethod]
    public void Decode_IsCaseInsensitive_OnTypeToken()
        => Assert.AreEqual(RegistryValueKind.DWord, RegistryValueCodec.Decode("dword", "5").Kind);

    [TestMethod]
    public void Decode_EmptyBinaryAndMultiString_AreEmptyArrays()
    {
        Assert.AreEqual(0, ((byte[])RegistryValueCodec.Decode("Binary", "").Data).Length);
        Assert.AreEqual(0, ((string[])RegistryValueCodec.Decode("MultiString", "").Data).Length);
    }

    [TestMethod]
    public void Decode_Dword_AcceptsUnsignedOverflow()
        => Assert.AreEqual(-1, RegistryValueCodec.Decode("DWord", "4294967295").Data);   // 0xFFFFFFFF → -1

    // ── User input parsing ───────────────────────────────────────────────────

    [TestMethod]
    public void ParseUserInput_Dword_AcceptsDecimalHexAndNegative()
    {
        Assert.AreEqual("26",  RegistryValueCodec.ParseUserInput("26",   RegistryValueKind.DWord));
        Assert.AreEqual("26",  RegistryValueCodec.ParseUserInput("0x1A", RegistryValueKind.DWord));
        Assert.AreEqual("-1",  RegistryValueCodec.ParseUserInput("-1",   RegistryValueKind.DWord));
        Assert.AreEqual("-1",  RegistryValueCodec.ParseUserInput("0xFFFFFFFF", RegistryValueKind.DWord));
    }

    [TestMethod]
    public void ParseUserInput_Qword_AcceptsHex()
        => Assert.AreEqual("16", RegistryValueCodec.ParseUserInput("0x10", RegistryValueKind.QWord));

    [TestMethod]
    public void ParseUserInput_Binary_IgnoresWhitespace()
        => Assert.AreEqual("01ABFF", RegistryValueCodec.ParseUserInput("01 AB FF", RegistryValueKind.Binary));

    [TestMethod]
    public void ParseUserInput_MultiString_SplitsLines()
        => Assert.AreEqual("a\0b\0c", RegistryValueCodec.ParseUserInput("a\r\nb\nc", RegistryValueKind.MultiString));

    [TestMethod]
    public void ParseUserInput_InvalidDword_Throws()
        => Assert.ThrowsExactly<FormatException>(() => RegistryValueCodec.ParseUserInput("not-a-number", RegistryValueKind.DWord));

    [TestMethod]
    public void ParseUserInput_InvalidBinary_Throws()
        => Assert.ThrowsExactly<FormatException>(() => RegistryValueCodec.ParseUserInput("ZZ", RegistryValueKind.Binary));

    // ── ToEditText seeds a box that ParseUserInput turns back into the wire form ──

    [TestMethod]
    [DataRow(RegistryValueKind.DWord)]
    [DataRow(RegistryValueKind.QWord)]
    [DataRow(RegistryValueKind.Binary)]
    [DataRow(RegistryValueKind.MultiString)]
    [DataRow(RegistryValueKind.String)]
    public void ToEditText_RoundTripsThroughParseUserInput(RegistryValueKind kind)
    {
        object data = kind switch
        {
            RegistryValueKind.DWord       => 26,
            RegistryValueKind.QWord       => 26L,
            RegistryValueKind.Binary      => new byte[] { 0x01, 0xAB },
            RegistryValueKind.MultiString => new[] { "a", "b" },
            _                             => "hello",
        };

        var seed = RegistryValueCodec.ToEditText(data, kind);
        Assert.AreEqual(RegistryValueCodec.Encode(data, kind), RegistryValueCodec.ParseUserInput(seed, kind));
    }
}
