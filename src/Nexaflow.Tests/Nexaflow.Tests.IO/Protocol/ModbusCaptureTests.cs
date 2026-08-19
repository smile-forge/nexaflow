using Nexaflow.IO.Protocol.Resolution;
using Nexaflow.IO.Protocol.Values;
using Nexaflow.IO.Protocol.Wire;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Protocol;

/// <summary>
/// Modbus TCP, against the corpus captures.
/// </summary>
/// <remarks>
/// The shape neither TCP nor NTP had: a fork whose arms are different lengths, inside the thing a length
/// field measures. So the length cannot be worked out until it is known which arm was taken, and which arm
/// was taken depends partly on something that is not in the message at all — a request and its response
/// carry the same function code, and you know which you are reading from who sent it.
/// </remarks>
[TestClass]
[NoCoverage("DynamicProtocol authored protocol definitions — engine structure, no single product node")]
public class ModbusCaptureTests
{
    private const long Asking = 0, Answering = 1;

    private static ProtocolFile.Loaded Modbus() => Definitions.Load("modbus");

    private static byte[] Capture(int which) => ProtocolCorpus.Get("modbus").Captures[which].Bytes;

    private static RunGraph Read(int which, long direction)
        => new GraphCodec(Modbus().Graph).Decode(
               Capture(which), new Dictionary<string, ProtoValue> { ["Direction"] = ProtoValue.Of(direction) });

    private static long Number(RunGraph run, string field)
        => run.Nodes.Single(n => n.Of is Field f && f.Id == field).Value.AsInt();

    [TestMethod]
    public void A_request_reads_as_the_breakdown_says()
    {
        var run = Read(0, Asking);

        Assert.AreEqual(1, Number(run, "transactionId"));
        Assert.AreEqual(0, Number(run, "protocolId"));
        Assert.AreEqual(6, Number(run, "length"), "unit identifier plus a five-octet PDU");
        Assert.AreEqual(17, Number(run, "unitId"));
        Assert.AreEqual(3, Number(run, "functionCode"), "Read Holding Registers");
        Assert.AreEqual(107, Number(run, "startingAddress"));
        Assert.AreEqual(3, Number(run, "quantityOfRegisters"));
    }

    [TestMethod]
    public void A_response_reads_as_the_breakdown_says()
    {
        var run = Read(1, Answering);

        Assert.AreEqual(9, Number(run, "length"));
        Assert.AreEqual(3, Number(run, "functionCode"), "echoed, top bit clear");
        Assert.AreEqual(6, Number(run, "byteCount"), "twice the three registers asked for");

        var data = run.Nodes.Single(n => n.Of is Field { Id: "registers" }).Value.AsBytes();
        Assert.AreEqual("AE41565243 40".Replace(" ", ""), Convert.ToHexString(data));
    }

    [TestMethod]
    public void An_exception_response_takes_the_other_arm()
    {
        // Same function code family, top bit set — and a body of one octet where the normal reply has
        // seven. The arms are different lengths, which is what makes the length field interesting.
        var run = Read(2, Answering);

        Assert.AreEqual(3, Number(run, "length"));
        Assert.AreEqual(0x83, Number(run, "functionCode"));
        Assert.AreEqual(2, Number(run, "exceptionCode"), "ILLEGAL DATA ADDRESS");
    }

    [TestMethod]
    [DataRow(0, Asking, "a request")]
    [DataRow(1, Answering, "a normal response")]
    [DataRow(2, Answering, "an exception response")]
    public void A_capture_written_back_is_the_same_octets(int which, long direction, string what)
    {
        var modbus = Modbus();
        var read = new GraphCodec(modbus.Graph).Decode(
            Capture(which), new Dictionary<string, ProtoValue> { ["Direction"] = ProtoValue.Of(direction) });

        Dictionary<string, ProtoValue> setting = new(StringComparer.Ordinal)
        {
            ["Direction"] = ProtoValue.Of(direction),
        };

        foreach (var field in modbus.Graph.Nodes.OfType<Field>())
            if (read.Nodes.SingleOrDefault(n => ReferenceEquals(n.Of, field)) is { } found
                && found.Has(Facet.Value))
                setting[field.As ?? field.Id] = found.Value;

        CollectionAssert.AreEqual(Capture(which), new GraphCodec(modbus.Graph).Encode(setting),
            $"{what} did not survive being read and written again");
    }
}
