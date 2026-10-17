using Nexaflow.IO.Usb.Descriptors;
using Nexaflow.IO.Usb.Descriptors.Layout;
using Nexaflow.Tests.Fixtures;
using static Nexaflow.Tests.IO.Usb.UsbFixtures;
using static Nexaflow.Tests.Fixtures.UsbSamples;

namespace Nexaflow.Tests.IO.Usb;

[TestClass]
[CoversNode("io-usb-descriptors")]
public class UsbDescriptorMapTests
{
    static readonly UsbDescriptorContext Lg = new(UsbSpeed.Full, i => i switch { 1 => "LG Electronics Inc.", 3 => "LG Monitor Controls", _ => null }, null);

    static UsbDescriptorMap LgConfig() => UsbDescriptorMaps.Configuration(Bytes(LgConfiguration), Lg);

    [TestMethod]
    public void EveryByteBelongsToExactlyOneDescriptor()
    {
        foreach (var map in new[] { LgConfig(), UsbDescriptorMaps.Configuration(Bytes(AsusLcdConfiguration), Lg), UsbDescriptorMaps.Bos(Bytes(PlatformBos), Lg), UsbDescriptorMaps.Bos(Bytes(TiHubBos), Lg) })
        {
            int expected = 0;
            foreach (var entry in map.Entries)
            {
                Assert.AreEqual(expected, entry.Offset, $"{map.Title}: a gap or overlap before {entry.Kind}");
                expected += entry.Length;
                foreach (var field in entry.Fields)
                    Assert.IsTrue(field.Offset >= entry.Offset && field.Offset + field.Size <= entry.Offset + entry.Length, $"{entry.Kind}.{field.Name} lies outside its descriptor");
            }
            Assert.AreEqual(map.Bytes.Length, expected, $"{map.Title}: bytes left unaccounted");
            Assert.AreEqual(0, map.Problems.Count, string.Join("; ", map.Problems));
        }
    }

    [TestMethod]
    public void Configuration_TreeFollowsTheDescriptors()
    {
        var map = LgConfig();
        string Shape(int? parent) => string.Join(",", map.ChildrenOf(parent).Select(e => $"{e.Kind}[{Shape(e.Index)}]"));

        Assert.AreEqual(
            "Configuration[Interface[HID[],Endpoint[],Endpoint[]],Interface association[" +
            "Interface[CDC Header[],CDC Call Management[],CDC Abstract Control Management[],CDC Union[],Endpoint[]],Interface[Endpoint[],Endpoint[]]]]",
            Shape(null));
    }

    [TestMethod]
    public void StandardFields_HaveTheirOffsetsValuesAndMeanings()
    {
        var device = UsbDescriptorMaps.Device(Bytes(LgDevice), Lg).Entries.Single();
        var vendor = device.Fields.Single(f => f.Name == "idVendor");
        Assert.AreEqual((8, 2, "0x043E (1086)"), (vendor.Offset, vendor.Size, vendor.Value));
        Assert.AreEqual("\"LG Monitor Controls\"", device.Fields.Single(f => f.Name == "iProduct").Meaning);
        Assert.IsNull(device.Fields.Single(f => f.Name == "iSerialNumber").Meaning, "a string the device did not give is not guessed at");
        Assert.AreEqual("2.00", device.Fields.Single(f => f.Name == "bcdUSB").Meaning);
        StringAssert.StartsWith(device.Summary, "USB 2.00 · 043E:9A39");

        var map = LgConfig();
        var config = map.Entries[0];
        Assert.AreEqual("none", config.Fields.Single(f => f.Name == "iConfiguration").Meaning, "index 0 means no string");
        Assert.AreEqual("reserved (set), self-powered", config.Fields.Single(f => f.Name == "bmAttributes").Meaning);
        var endpoint = map.EntryAt(80)!;
        Assert.AreEqual("Endpoint", endpoint.Kind);
        Assert.AreEqual("EP 3 IN", endpoint.Fields.Single(f => f.Name == "bEndpointAddress").Meaning);
        Assert.AreEqual("2 ms", endpoint.Fields.Single(f => f.Name == "bInterval").Meaning, "full-speed interrupt counts frames");
        Assert.AreEqual("bInterval", map.FieldAt(83)!.Name);
    }

    [TestMethod]
    public void ClassDescriptors_AreDecodedInTheirInterface()
    {
        var map = LgConfig();
        var hid = map.Entries.Single(e => e.Kind == "HID");
        Assert.AreEqual("HID 1.11 · report descriptor 33 bytes", hid.Summary);

        var union = map.Entries.Single(e => e.Kind == "CDC Union");
        Assert.AreEqual("control 1 → 2", union.Summary);
        var call = map.Entries.Single(e => e.Kind == "CDC Call Management");
        StringAssert.Contains(call.Summary, "data on interface 2");
        var acm = map.Entries.Single(e => e.Kind == "CDC Abstract Control Management");
        Assert.AreEqual("ACM · line coding + serial state", acm.Summary);
    }

    [TestMethod]
    public void AudioDescriptors_FollowTheirVersion()
    {
        // UAC1: an AC interface with header, a microphone input terminal; an AS interface with a type I format.
        var bytes = Bytes(
            "09 02 4B 00 02 01 00 80 32 " +
            "09 04 00 00 00 01 01 00 00 " +
            "09 24 01 00 01 1E 00 01 01 " +
            "0C 24 02 01 01 02 00 01 00 00 00 00 " +
            "09 04 01 00 01 01 02 00 00 " +
            "07 24 01 01 01 01 00 " +
            "0B 24 02 01 02 02 10 01 80 BB 00 " +
            "09 05 81 05 C0 00 01 00 00");
        var map = UsbDescriptorMaps.Configuration(bytes, UsbDescriptorContext.Bare with { Speed = UsbSpeed.Full });

        Assert.AreEqual("Audio 1.00 · 1 streaming interface(s)", map.Entries.Single(e => e.Kind == "Audio control header").Summary);
        Assert.AreEqual("terminal 1 · microphone · 1 channel(s)", map.Entries.Single(e => e.Kind == "Audio input terminal").Summary);
        Assert.AreEqual("PCM · terminal 1", map.Entries.Single(e => e.Kind == "Audio streaming general").Summary);
        Assert.AreEqual("2 ch × 16-bit @ 48 kHz", map.Entries.Single(e => e.Kind == "Audio format type").Summary);
        var endpoint = map.Entries.Single(e => e.Type == UsbDescriptorType.Endpoint);
        CollectionAssert.Contains(endpoint.Fields.Select(f => f.Name).ToList(), "bSynchAddress", "Audio 1.0 endpoints carry two more bytes");
    }

    [TestMethod]
    public void Bos_NamesItsPlatformCapabilities()
    {
        var map = UsbDescriptorMaps.Bos(Bytes(PlatformBos), UsbDescriptorContext.Bare);
        var os20 = map.Entries.Single(e => e.Kind == "Microsoft OS 2.0");
        Assert.AreEqual("0x01", os20.Fields.Single(f => f.Name == "bMS_VendorCode").Value);
        Assert.AreEqual("Windows 0x06030000 · set 178 bytes · vendor code 0x01", os20.Summary);
        Assert.AreEqual("Microsoft OS 2.0", os20.Fields.Single(f => f.Name == "PlatformCapabilityUUID").Meaning);
        Assert.IsTrue(map.Entries.Any(e => e.Kind == "WebUSB"));
        Assert.IsTrue(map.ChildrenOf(0).Count() == 2, "capabilities sit under the BOS header");
    }

    [TestMethod]
    public void MalformedTail_IsKeptAsBytesAndNamed()
    {
        // A configuration whose last descriptor claims more bytes than were returned.
        var map = UsbDescriptorMaps.Configuration(Bytes("09 02 14 00 01 01 00 80 32 09 04 00 00 00 FF 00 00 00 07 05"), UsbDescriptorContext.Bare);
        var last = map.Entries[^1];
        Assert.AreEqual("Malformed", last.Kind);
        Assert.AreEqual((18, 2), (last.Offset, last.Length));
        Assert.IsTrue(map.Problems.Any(p => p.Contains("bLength 7")));
    }

    [TestMethod]
    public void ShortDescriptor_EndsInOneTruncatedField()
    {
        // An endpoint descriptor that stops after wMaxPacketSize's first byte.
        var map = UsbDescriptorMaps.Configuration(Bytes("09 02 10 00 01 01 00 80 32 05 05 81 02 00 02 00"), UsbDescriptorContext.Bare);
        var endpoint = map.Entries[1];
        var truncated = endpoint.Fields[^1];
        Assert.AreEqual("wMaxPacketSize", truncated.Name);
        Assert.AreEqual(1, truncated.Size);
        StringAssert.StartsWith(truncated.Meaning, "truncated");
    }

    [TestMethod]
    public void ADecoderListedFirst_ClaimsItsDescriptors()
    {
        var vendor = new VendorDecoder();
        var map = UsbDescriptorMaps.Configuration(Bytes("09 02 0E 00 01 01 00 80 32 05 41 01 02 03"), UsbDescriptorContext.Bare, [vendor, .. UsbDescriptorMaps.Default]);
        Assert.AreEqual("Acme thing", map.Entries[1].Kind);
        Assert.AreEqual("bAcme", map.Entries[1].Fields[2].Name);
    }

    [TestMethod]
    public void Hex_IsSpacedUppercase()
    {
        Assert.AreEqual("", UsbHex.Format([]));
        Assert.AreEqual("0A FF 00", UsbHex.Format([0x0A, 0xFF, 0x00]));
    }

    sealed class VendorDecoder : IUsbDescriptorDecoder
    {
        public UsbDescriptorHeading? TryDecode(UsbFieldWriter fields, UsbDescriptorScope scope)
        {
            if (fields.Type != 0x41) return null;
            fields.U8("bLength");
            fields.U8("bDescriptorType");
            fields.U8("bAcme");
            return new("Acme thing", "vendor");
        }
    }
}
