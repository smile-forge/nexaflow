using Nexaflow.Providers.Common;

namespace Nexaflow.Tests.Providers;

[TestClass]
public class EndpointResidencyTests
{
    [TestMethod]
    [DataRow("http://localhost:11434")]
    [DataRow("http://127.0.0.1:1234/v1")]
    [DataRow("http://127.4.5.6")]
    [DataRow("http://[::1]:8080")]
    public void ALoopbackEndpoint_KeepsRequestsOnThisMachine(string endpoint)
        => Assert.AreEqual(DataResidency.StaysOnMachine, EndpointResidency.Of(endpoint, DataResidency.LeavesMachine));

    [TestMethod]
    [DataRow("https://api.anthropic.com")]
    [DataRow("http://192.168.1.20:11434")]
    [DataRow("http://gpu-box:11434")]
    [DataRow("not a url")]
    public void AnyOtherEndpoint_TakesThemOffIt(string endpoint)
        => Assert.AreEqual(DataResidency.LeavesMachine, EndpointResidency.Of(endpoint, DataResidency.StaysOnMachine));

    [TestMethod]
    public void NoEndpoint_IsTheProvidersOwnDefault()
    {
        Assert.AreEqual(DataResidency.LeavesMachine,  EndpointResidency.Of("", DataResidency.LeavesMachine));
        Assert.AreEqual(DataResidency.StaysOnMachine, EndpointResidency.Of(null, DataResidency.StaysOnMachine));
    }
}
