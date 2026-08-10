using System.IO;
using Nexaflow.IO.Protocol.Wire;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Protocol;

/// <summary>
/// Every built document written out as English, and the files put somewhere they can be read beside a
/// specification.
///
/// <para>
/// The corpus was assembled by the same hand that wrote the engine, so it can only contain the gaps that
/// hand thought of. A description that can be laid next to an RFC can be found wrong <b>by the RFC</b> —
/// which is the only external oracle available here, and the reason this exists as an artefact rather than
/// as a debugging aid.
/// </para>
/// </summary>
[TestClass]
[NoCoverage("generates the review artefact for each document; the assertions guard the generator")]
public class ProtocolProseTests
{
    /// <summary>Written where something outside the test run can pick them up.</summary>
    internal static string OutputDirectory =>
        Path.Combine(Path.GetTempPath(), "nexaflow-protocol-prose");

    private static (string File, string Title, MessageDef Message)[] Documents =>
    [
        ("ntp-timesync.md",      "NTPv4 (RFC 5905)",           EndToEndCaptureTests.Definition()),
        ("modbus-request.md",    "Modbus TCP request",         FramedChoiceCaptureTests.Request()),
        ("modbus-response.md",   "Modbus TCP response",        FramedChoiceCaptureTests.Response()),
        ("mqtt-connect.md",      "MQTT 3.1.1 CONNECT",         VariableWidthCaptureTests.Connect()),
        ("mqtt-subscribe.md",    "MQTT 3.1.1 SUBSCRIBE",       VariableWidthCaptureTests.FilterList()),
        ("snmp-message.md",      "SNMPv2c (RFC 3416/3417)",    NestedLengthCaptureTests.Message()),
    ];

    [TestMethod]
    public void Every_document_describes_itself_in_prose()
    {
        Directory.CreateDirectory(OutputDirectory);

        foreach (var (file, title, message) in Documents)
        {
            var prose = MessageProse.Describe(message);

            File.WriteAllText(Path.Combine(OutputDirectory, file),
                $"<!-- Generated from the DynamicProtocol document '{message.Id}'.\n"
              + $"     Intended to be read against: {title} -->\n\n" + prose);

            Assert.IsTrue(prose.Length > 400, $"{file} is too short to review");
            StringAssert.Contains(prose, "## Layout", file);
            StringAssert.Contains(prose, "## What is refused", file);
        }
    }

    [TestMethod]
    public void The_description_says_which_values_the_caller_supplies_and_which_are_derived()
    {
        // The single most important thing a reviewer needs, and the one a field dump never states: a
        // length that is supplied rather than measured is a bug the reader cannot see.
        var prose = MessageProse.Describe(FramedChoiceCaptureTests.Response());

        StringAssert.Contains(prose, "## Supplied by the caller");
        StringAssert.Contains(prose, "`inputs.functionCode`");

        StringAssert.Contains(prose, "## Derived by the engine");
        StringAssert.Contains(prose, "`length` — fields.body.extent");
        StringAssert.Contains(prose, "`byteCount` — fields.registers.extent");
    }

    [TestMethod]
    public void The_description_states_what_decides_a_branch_and_what_ends_a_chain()
    {
        var prose = MessageProse.Describe(FramedChoiceCaptureTests.Response());

        StringAssert.Contains(prose, "fields.functionCode.value & 0x80");
        StringAssert.Contains(prose, "taken when the discriminator is 0 (0x0)");
        StringAssert.Contains(prose, "taken when the discriminator is 128 (0x80)");
        StringAssert.Contains(prose, "Another follows while `ordinal < (fields.byteCount.value / 2)`");

        // And that the cover is proved rather than hoped for.
        StringAssert.Contains(prose, "an unhandled value is impossible");
    }

    [TestMethod]
    public void The_description_names_a_document_transform_and_the_domain_it_is_reversible_over()
    {
        // A transform is where a protocol's own arithmetic lives, so a reviewer has to be told it is there
        // and told where it stops being invertible.
        var prose = MessageProse.Describe(NestedLengthCaptureTests.Message());

        StringAssert.Contains(prose, "document transform `objectIdentifier`");
        StringAssert.Contains(prose, "only reversible where");
    }

    [TestMethod]
    public void A_width_that_depends_on_a_value_is_called_out_as_such()
    {
        var prose = MessageProse.Describe(VariableWidthCaptureTests.Connect());

        StringAssert.Contains(prose, "**Its width is a function of its value**");
        StringAssert.Contains(prose, "shortest encoding of its value is refused");
    }

    [TestMethod]
    public void An_expression_reads_back_as_it_was_written()
    {
        // Records synthesise a ToString that prints the whole tree; an explanation built on that is
        // unreadable, and an unreadable explanation is not review material.
        foreach (var (source, expected) in ((string, string)[])
        [
            ("fields.byteCount.value / 2",              "fields.byteCount.value / 2"),
            ("fields.functionCode.value band 0x80",     "fields.functionCode.value & 0x80"),
            ("room > 0",                                "room > 0"),
            ("inputs.registers",                        "inputs.registers"),
            ("value |> split('.') |> count()",          "value |> split('.') |> count()"),
            ("a ? b : c",                               "a ? b : c"),
            ("let x = 1 in x + 2",                      "let x = 1 in x + 2"),
            ("xs |> map(v -> v * 2)",                   "xs |> map(v -> v * 2)"),
        ])
            Assert.AreEqual(expected, Nexaflow.IO.Protocol.Expressions.Expr.Parse(source).Render(), source);
    }
}
