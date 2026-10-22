using System.IO;
using Nexaflow.IO.Protocol.Resolution;
using Nexaflow.IO.Protocol.Values;
using Nexaflow.IO.Protocol.Wire;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Network;

/// <summary>
/// The magic packet, as <c>Protocols/wol.json</c> describes it — the copy the action ships with, not one
/// kept beside the tests, so what is proven here is what goes on the wire.
/// </summary>
/// <remarks>
/// The packets below are spelled out from the white paper, octet by octet, rather than produced by anything
/// in the product: six of 0xFF, the address sixteen times, and a password where there is one. A
/// description that agreed with itself and not with the format would pass a test that let it write the
/// expected bytes.
/// </remarks>
[TestClass]
[CoversNode("network-action-wol")]
public class WakeOnLanPacketTests
{
    private const string Mac = "00:1a:2b:3c:4d:5e";

    private static readonly byte[] Address = [0x00, 0x1a, 0x2b, 0x3c, 0x4d, 0x5e];

    private static ProtocolFile.Loaded Wol()
        => ProtocolFile.Read(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Protocols", "wol.json")));

    /// <summary>Six octets of all ones, the address sixteen times, then whatever is given.</summary>
    private static byte[] Packet(byte[] address, params byte[] password)
        => [.. Enumerable.Repeat((byte)0xff, 6), .. Enumerable.Repeat(address, 16).SelectMany(a => a), .. password];

    private static RunGraph Read(byte[] packet) => new GraphCodec(Wol().Graph).Decode(packet);

    private static RunNode? Place(RunGraph run, string field)
        => run.Nodes.SingleOrDefault(n => n.Of is Field f && f.Id == field && n.Has(Facet.Value));

    /// <summary>The SecureOn password, whichever of its two widths the packet took.</summary>
    private static RunNode? Password(RunGraph run) => Place(run, "password4") ?? Place(run, "password6");

    // ── What the wire said ────────────────────────────────────────────────────

    [TestMethod]
    public void A_packet_reads_as_the_white_paper_says()
    {
        var run = Read(Packet(Address));

        CollectionAssert.AreEqual(Enumerable.Repeat((byte)0xff, 6).ToArray(), Place(run, "sync")!.Value.AsBytes());
        CollectionAssert.AreEqual(Enumerable.Repeat(Address, 16).SelectMany(a => a).ToArray(),
                                  Place(run, "copies")!.Value.AsBytes());
        Assert.IsNull(Password(run), "nothing followed the sixteenth copy, so there is no password");
    }

    [TestMethod]
    [DataRow(4)]
    [DataRow(6)]
    public void Octets_after_the_copies_are_a_SecureOn_password(int width)
    {
        var password = Enumerable.Range(0xa0, width).Select(b => (byte)b).ToArray();

        var run = Read(Packet(Address, password));

        CollectionAssert.AreEqual(password, Password(run)!.Value.AsBytes());
    }

    // ── And what it writes ────────────────────────────────────────────────────

    [TestMethod]
    public void An_address_is_all_it_takes_to_write_one()
    {
        // The caller says which adapter and nothing else. The ones, the sixteen and the order are the
        // description's, which is the whole reason the action holds no octets.
        var written = new GraphCodec(Wol().Graph).Encode(
            new Dictionary<string, ProtoValue> { ["Target MAC"] = ProtoValue.Of(Mac) });

        CollectionAssert.AreEqual(Packet(Address), written);
    }

    [TestMethod]
    public void A_target_with_a_password_gets_it_after_the_copies()
    {
        byte[] password = [0x01, 0x02, 0x03, 0x04, 0x05, 0x06];

        var written = new GraphCodec(Wol().Graph).Encode(new Dictionary<string, ProtoValue>
        {
            ["Target MAC"] = ProtoValue.Of(Mac),
            ["SecureOn Password"] = ProtoValue.Of(password),
        });

        CollectionAssert.AreEqual(Packet(Address, password), written);
    }

    // ── What is not a magic packet ────────────────────────────────────────────

    [TestMethod]
    public void Copies_that_disagree_are_refused()
    {
        // One octet of the ninth copy. Every width is right, which is the point — an adapter would not wake
        // for this, so a reader that called it a magic packet would be describing a packet that does nothing.
        var wrong = Packet(Address);
        wrong[6 + 8 * 6 + 3] ^= 0x01;

        var refused = Assert.ThrowsExactly<ProtoTypeException>(() => Read(wrong));

        StringAssert.Contains(refused.Message, "sixteen");
    }

    [TestMethod]
    public void A_synchronization_stream_that_is_not_all_ones_is_refused()
    {
        var wrong = Packet(Address);
        wrong[2] = 0xfe;

        var refused = Assert.ThrowsExactly<ProtoTypeException>(() => Read(wrong));

        StringAssert.Contains(refused.Message, "sync");
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(5)]
    [DataRow(7)]
    public void A_password_of_any_other_width_is_refused(int width)
    {
        var refused = Assert.ThrowsExactly<ProtoTypeException>(
            () => Read(Packet(Address, new byte[width])));

        // No arm of the fork after the copies takes this many octets, so the packet is refused where its end
        // is, naming the place and the shapes it offers.
        StringAssert.Contains(refused.Message, "secureOn");
        StringAssert.Contains(refused.Message, "Offered: 0, 4, 6");
    }

    [TestMethod]
    public void And_a_password_of_another_width_is_not_written()
    {
        // The same fork going out: a target configured with something that is not a SecureOn password finds
        // out here rather than by not waking.
        var refused = Assert.ThrowsExactly<ProtoTypeException>(() => new GraphCodec(Wol().Graph).Encode(
            new Dictionary<string, ProtoValue>
            {
                ["Target MAC"] = ProtoValue.Of(Mac),
                ["SecureOn Password"] = ProtoValue.Of(new byte[5]),
            }));

        StringAssert.Contains(refused.Message, "secureOn");
    }
}
