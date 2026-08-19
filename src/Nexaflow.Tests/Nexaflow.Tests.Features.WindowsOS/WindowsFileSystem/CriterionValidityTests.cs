using Nexaflow.Features.WindowsFileSystem.FileActions;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.WindowsFileSystem;

[TestClass]
public class CriterionValidityTests
{
    [TestMethod]
    [CoversNode("winfs-external-apps-editor")]
    [CoversNode("winfs-filemap-editor")]
    public void Extension_RejectsPathLikeValues()
    {
        Assert.IsTrue(CriterionValidity.IsValid("Extension", "*.slnx"));
        Assert.IsTrue(CriterionValidity.IsValid("Extension", ".slnx"));
        Assert.IsFalse(CriterionValidity.IsValid("Extension", @"C:\src\*.cs"));   // contains a path separator
        Assert.IsFalse(CriterionValidity.IsValid("Extension", "src/*.cs"));        // forward separator
        Assert.IsFalse(CriterionValidity.IsValid("Extension", @"**\*.cs"));        // recursive glob
    }

    [TestMethod]
    [CoversNode("winfs-external-apps-editor")]
    [CoversNode("winfs-filemap-editor")]
    public void Empty_IsValid_AndPathPatternIsLenient()
    {
        Assert.IsTrue(CriterionValidity.IsValid("Extension", ""));
        Assert.IsTrue(CriterionValidity.IsValid("Extension", "   "));
        Assert.IsTrue(CriterionValidity.IsValid("PathPattern", @"C:\src\**\*.cs"));
        Assert.IsTrue(CriterionValidity.IsValid("PathPattern", "anything goes"));
    }
}
