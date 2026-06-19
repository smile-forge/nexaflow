using Nexaflow.Features.WindowsFileSystem.FileActions;

namespace Nexaflow.Tests.Features.WindowsFileSystem;

[TestClass]
public class CriterionValidityTests
{
    [TestMethod]
    public void Extension_RejectsPathLikeValues()
    {
        Assert.IsTrue(CriterionValidity.IsValid("Extension", "*.slnx"));
        Assert.IsTrue(CriterionValidity.IsValid("Extension", ".slnx"));
        Assert.IsFalse(CriterionValidity.IsValid("Extension", @"C:\src\*.cs"));   // contains a path separator
        Assert.IsFalse(CriterionValidity.IsValid("Extension", "src/*.cs"));        // forward separator
        Assert.IsFalse(CriterionValidity.IsValid("Extension", @"**\*.cs"));        // recursive glob
    }

    [TestMethod]
    public void Empty_IsValid_AndPathPatternIsLenient()
    {
        Assert.IsTrue(CriterionValidity.IsValid("Extension", ""));
        Assert.IsTrue(CriterionValidity.IsValid("Extension", "   "));
        Assert.IsTrue(CriterionValidity.IsValid("PathPattern", @"C:\src\**\*.cs"));
        Assert.IsTrue(CriterionValidity.IsValid("PathPattern", "anything goes"));
    }
}
