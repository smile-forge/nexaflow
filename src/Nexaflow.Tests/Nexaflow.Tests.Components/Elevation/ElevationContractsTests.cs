using System.Collections.Generic;
using System.Text.Json;
using Nexaflow.Elevation.Contracts;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Components.Elevation;

[TestClass]
[CoversNode("sysinfo-elevation")]
public class ElevationContractsTests
{
    [TestMethod]
    public void Request_RoundTrips_PreservingOrderAndArgs()
    {
        var req = new ElevatedRequest
        {
            Operations =
            {
                new ElevatedOperation { Op = ElevatedOps.ServiceStart, Args = { ["serviceName"] = "Spooler" } },
                new ElevatedOperation { Op = ElevatedOps.EnvSet,       Args = { ["name"] = "FOO", ["value"] = "bar" } },
            },
        };

        var json = JsonSerializer.Serialize(req, ElevationJson.Options);
        var back = JsonSerializer.Deserialize<ElevatedRequest>(json, ElevationJson.Options)!;

        Assert.AreEqual(2, back.Operations.Count);
        Assert.AreEqual(ElevatedOps.ServiceStart, back.Operations[0].Op);
        Assert.AreEqual("Spooler", back.Operations[0].Args["serviceName"]);
        Assert.AreEqual("bar", back.Operations[1].Args["value"]);
    }

    [TestMethod]
    public void Result_SerializesErrorKindAsString()
    {
        var json = JsonSerializer.Serialize(ElevatedResult.Declined(), ElevationJson.Options);
        // Enum-as-string keeps the wire format stable across both ends.
        StringAssert.Contains(json, "UserDeclinedElevation");
    }

    [TestMethod]
    public void FromOperations_AllSucceed_OverallSuccess()
    {
        var res = ElevatedResult.FromOperations(
        [
            ElevatedOperationResult.Ok("a", "ok"),
            ElevatedOperationResult.Ok("b", "ok"),
        ]);

        Assert.IsTrue(res.Success);
        Assert.AreEqual(ElevatedErrorKind.None, res.ErrorKind);
    }

    [TestMethod]
    public void FromOperations_SurfacesFirstFailure()
    {
        var res = ElevatedResult.FromOperations(
        [
            ElevatedOperationResult.Ok("a", "ok"),
            ElevatedOperationResult.Fail("b", ElevatedErrorKind.OperationFailed, "boom"),
        ]);

        Assert.IsFalse(res.Success);
        Assert.AreEqual(ElevatedErrorKind.OperationFailed, res.ErrorKind);
        StringAssert.Contains(res.Message, "boom");
    }

    [TestMethod]
    public void Declined_WasDeclined_True() =>
        Assert.IsTrue(ElevatedResult.Declined().WasDeclined);
}
