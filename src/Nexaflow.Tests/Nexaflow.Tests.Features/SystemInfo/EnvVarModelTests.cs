using System.Linq;
using Nexaflow.Features.SystemInfo.Models;

using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.SystemInfo;

[TestClass]
[CoversNode("sysinfo-envvars")]
public class EnvVarModelTests
{
    [TestMethod]
    public void Entries_SplitOnSemicolon_PreservingOrder()
    {
        var row = new EnvVarRow("Path", @"C:\a;C:\b;C:\c", EnvScope.User);

        CollectionAssert.AreEqual(new[] { @"C:\a", @"C:\b", @"C:\c" }, row.Entries.ToArray());
        Assert.IsTrue(row.IsPathLike);
    }

    [TestMethod]
    public void PlainValue_IsNotPathLike()
    {
        var row = new EnvVarRow("FOO", "bar", EnvScope.User);

        Assert.IsFalse(row.IsPathLike);
        Assert.AreEqual(1, row.Entries.Count);
    }

    [TestMethod]
    public void Scope_DrivesElevation()
    {
        Assert.IsTrue(new EnvVarRow("X", "", EnvScope.Machine).RequiresElevation);
        Assert.IsFalse(new EnvVarRow("X", "", EnvScope.User).RequiresElevation);
    }
}
