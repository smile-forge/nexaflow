using System.Threading.Tasks;
using Nexaflow.Core.Services.Elevation;
using Nexaflow.Elevation.Contracts;

namespace Nexaflow.Tests.Core.Unit.Elevation;

[TestClass]
public class ElevatedBridgeLauncherTests
{
    // Both paths are deterministic and headless-safe: no elevated process is ever launched.
    private static ElevatedBridgeLauncher Missing() =>
        new(bridgePath: @"C:\nexaflow\does-not-exist\Nexaflow.PrivilegeBridge.exe");

    [TestMethod]
    public async Task EmptyRequest_ReturnsUnexpected()
    {
        var res = await Missing().RunAsync(new ElevatedRequest());

        Assert.IsFalse(res.Success);
        Assert.AreEqual(ElevatedErrorKind.Unexpected, res.ErrorKind);
    }

    [TestMethod]
    public async Task MissingBridge_ReturnsBridgeNotFound()
    {
        var res = await Missing().RunAsync(
            ElevatedRequest.Single(ElevatedOps.ServiceStart, (ElevatedArgs.ServiceName, "Spooler")));

        Assert.IsFalse(res.Success);
        Assert.AreEqual(ElevatedErrorKind.BridgeNotFound, res.ErrorKind);
        Assert.IsFalse(res.WasDeclined);
    }
}
