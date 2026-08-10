using Nexaflow.IO.Protocol.Converters;
using Nexaflow.Tests.Fixtures;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Nexaflow.Tests.Features.Protocol;

/// <summary>
/// The generalisation guard.
///
/// <para>
/// The DynamicProtocol engine may name <b>notions</b> — length-prefixed, interchangeable, escaped-inline,
/// back-reference — and may never name a <b>protocol</b>. A protocol's specifics belong in a document or a
/// pattern parameter. The moment a <c>Dhcp</c>, <c>CoapOption</c> or <c>BerLength</c> element appears in
/// the engine, the generalisation has failed and the engine has quietly become a pile of protocol
/// implementations wearing a shared name.
/// </para>
///
/// <para>
/// That rule is easy to state, easy to agree with, and easy to erode one plausible special case at a time,
/// so it is enforced here rather than trusted. The check is on <b>identifiers</b> — type names, member
/// names, enum values, and the engine's own registered vocabulary. Explanatory prose is deliberately
/// exempt: "SNMP request-id 821915 must be 0c 8a 9b, three octets" is exactly how a notion should be
/// justified, and forbidding it would make the code worse.
/// </para>
/// </summary>
[TestClass]
[NoCoverage("architecture guard — enforces the model's no-protocol-specifics rule")]
public class GeneralisationGuardTests
{
    /// <summary>
    /// Protocol names the engine must not contain as identifier words. Seeded from the corpus (those are
    /// the ten the model is validated against) plus the ones most likely to be reached for next.
    /// </summary>
    private static readonly string[] ProtocolNames =
    [
        // the corpus
        "snmp", "mdns", "dns", "modbus", "coap", "dhcp", "mqtt", "ntp", "ssdp", "tls", "bacnet",
        // encodings and neighbours that are really one protocol's mechanism
        "ber", "der", "asn1", "http", "https", "bootp", "upnp", "llmnr", "netbios",
        // the ones the roadmap names, so they are guarded before they arrive
        "zigbee", "zwave", "matter", "knx", "lldp", "cdp", "smb", "winrm", "wmi", "ssh", "ldap", "arp",
    ];

    /// <summary>
    /// Splits an identifier into words, handling camelCase, PascalCase, acronym runs and underscores.
    ///
    /// <para>
    /// Word-boundary matching rather than substring matching is essential: <c>Member</c> and <c>Number</c>
    /// both contain "ber", and a substring check would fail on the type system itself while catching
    /// nothing real.
    /// </para>
    /// </summary>
    private static IEnumerable<string> Words(string identifier)
        => Regex.Split(identifier, @"(?<!^)(?=[A-Z][a-z])|(?<=[a-z0-9])(?=[A-Z])|[_`.]|(?<=\D)(?=\d)|(?<=\d)(?=\D)")
                .Where(w => w.Length > 0)
                .Select(w => w.ToLowerInvariant());

    private static string? OffendingWord(string identifier)
        => Words(identifier).FirstOrDefault(w => ProtocolNames.Contains(w));

    private static Assembly Engine => typeof(ConverterTable).Assembly;

    [TestMethod]
    public void No_engine_type_is_named_after_a_protocol()
    {
        List<string> offenders = [];

        foreach (var type in Engine.GetTypes())
        {
            var name = type.FullName ?? type.Name;
            if (OffendingWord(name) is { } word)
                offenders.Add($"type '{name}' contains the protocol name '{word}'");
        }

        Assert.AreEqual(0, offenders.Count, Explain(offenders));
    }

    [TestMethod]
    public void No_engine_member_is_named_after_a_protocol()
    {
        List<string> offenders = [];

        foreach (var type in Engine.GetTypes())
            foreach (var member in type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic
                                                   | BindingFlags.Instance | BindingFlags.Static
                                                   | BindingFlags.DeclaredOnly))
            {
                // Compiler-generated plumbing (record equality, backing fields, closures) is not authored
                // vocabulary and would only produce noise.
                if (member.Name.Contains('<') || member.Name.StartsWith("get_") || member.Name.StartsWith("set_"))
                    continue;

                if (OffendingWord(member.Name) is { } word)
                    offenders.Add($"{type.Name}.{member.Name} contains the protocol name '{word}'");
            }

        Assert.AreEqual(0, offenders.Count, Explain(offenders));
    }

    [TestMethod]
    public void No_enum_value_in_the_engine_is_named_after_a_protocol()
    {
        // Enum members are the likeliest place a special case hides: an `AssemblerKind.CoapBlockwise`
        // reads as harmless taxonomy and is in fact three protocol implementations in a switch.
        List<string> offenders = [];

        foreach (var type in Engine.GetTypes().Where(t => t.IsEnum))
            foreach (var name in Enum.GetNames(type))
                if (OffendingWord(name) is { } word)
                    offenders.Add($"{type.Name}.{name} contains the protocol name '{word}'");

        Assert.AreEqual(0, offenders.Count, Explain(offenders));
    }

    [TestMethod]
    public void No_registered_converter_is_named_after_a_protocol()
    {
        // The converter table is the engine's own vocabulary — data rather than identifiers, and just as
        // load-bearing. A `berLength` or `coapDelta` converter is a protocol implementation with a
        // friendly name.
        List<string> offenders = [];

        foreach (var converter in ConverterTable.Default.All)
            if (OffendingWord(converter.Name) is { } word)
                offenders.Add($"converter '{converter.Name}' contains the protocol name '{word}'");

        Assert.AreEqual(0, offenders.Count, Explain(offenders));
    }

    [TestMethod]
    public void The_word_splitter_does_not_fire_on_ordinary_vocabulary()
    {
        // Guards the guard. "Member" and "Number" both contain "ber"; a substring check would fail on the
        // type system while catching nothing real, and the natural fix — suppressing the rule — would
        // disable it entirely.
        foreach (var benign in (string[])["Member", "Number", "NumberOfThings", "Remaining", "Descriptor",
                                          "Cardinality", "Transform", "Segment", "EntityMember"])
            Assert.IsNull(OffendingWord(benign), $"'{benign}' must not be flagged");

        // …and still catches the real thing in every casing it might arrive in.
        foreach (var offender in (string[])["DhcpOption", "parseCoap", "BER_LENGTH", "SnmpVarBind",
                                            "TlsExtension", "decodeMdnsName", "NtpTimestamp"])
            Assert.IsNotNull(OffendingWord(offender), $"'{offender}' must be flagged");
    }

    [TestMethod]
    public void The_engine_references_nothing_but_the_base_class_library()
    {
        // Containment, structurally: the engine has no socket API in scope, so a protocol document cannot
        // reach the network from here even by accident. A new ProjectReference is how that quietly stops
        // being true.
        var refs = Engine.GetReferencedAssemblies()
            .Select(a => a.Name ?? "")
            .Where(n => n.StartsWith("Nexaflow", StringComparison.Ordinal))
            .ToList();

        Assert.AreEqual(0, refs.Count,
            "Nexaflow.IO.Protocol must stay a zero-reference leaf — that is what makes it impossible for "
          + "an AI-authored document to reach a socket. Found: " + string.Join(", ", refs));
    }

    private static string Explain(List<string> offenders)
        => offenders.Count == 0 ? "" :
            "The protocol engine must deal in NOTIONS, never in protocol specifics — a protocol's details "
          + "belong in a document or a pattern parameter, never in the engine.\n\n"
          + string.Join("\n", offenders.Select(o => "  • " + o))
          + "\n\nIf a notion can only be described by naming a protocol, it is not yet a notion. Find the "
          + "general shape (for instance 'escaped-inline' rather than 'BER length form'), or leave the "
          + "protocol unexpressible and say so. Do not suppress this rule — explanatory comments naming a "
          + "protocol as an example are already exempt; this fires on identifiers only.";
}
