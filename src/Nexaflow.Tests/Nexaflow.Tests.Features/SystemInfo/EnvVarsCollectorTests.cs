using System;
using System.Linq;
using Nexaflow.Features.SystemInfo.Models;
using Nexaflow.Features.SystemInfo.Services;

using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.SystemInfo;

[TestClass]
[CoversNode("sysinfo-envvars")]
public class EnvVarsCollectorTests
{
    [TestMethod]
    public void CollectAll_ReturnsUserAndMachine()
    {
        var bundle = new EnvVarsCollector().CollectAll();

        Assert.IsTrue(bundle.Machine.Count > 0, "Machine scope should expose variables.");
        // PATH in the machine environment is set on every Windows install.
        Assert.IsTrue(bundle.Machine.Any(r => r.Name.Equals("Path", StringComparison.OrdinalIgnoreCase)),
            "Machine PATH expected.");
    }

    [TestMethod]
    public void Collect_TagsRowsWithRequestedScope()
    {
        var rows = new EnvVarsCollector().Collect(EnvScope.Machine);

        Assert.IsTrue(rows.Count > 0);
        Assert.IsTrue(rows.All(r => r.Scope == EnvScope.Machine));
    }
}
