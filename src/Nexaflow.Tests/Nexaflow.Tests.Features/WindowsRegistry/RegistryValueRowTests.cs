using Microsoft.Win32;
using Nexaflow.Features.WindowsRegistry.ViewModels;

namespace Nexaflow.Tests.Features.WindowsRegistry;

/// <summary>The right-pane value row (<see cref="RegistryValue"/>) — display name, type label, formatted data.</summary>
[TestClass]
public class RegistryValueRowTests
{
    [TestMethod]
    public void DefaultValue_ShowsParenDefault_AndIsDefault()
    {
        var v = new RegistryValue("", RegistryValueKind.String, "data");
        Assert.AreEqual("(Default)", v.Name);
        Assert.IsTrue(v.IsDefault);
        Assert.AreEqual("", v.RawName);
    }

    [TestMethod]
    public void NamedDword_FormatsTypeLabelAndData()
    {
        var v = new RegistryValue("Flags", RegistryValueKind.DWord, 26);
        Assert.AreEqual("Flags", v.Name);
        Assert.IsFalse(v.IsDefault);
        Assert.AreEqual("REG_DWORD", v.TypeLabel);
        Assert.AreEqual("0x0000001a (26)", v.Data);
    }

    [TestMethod]
    public void BinaryValue_FormatsAsHex()
    {
        var v = new RegistryValue("Blob", RegistryValueKind.Binary, new byte[] { 0x01, 0xAB });
        Assert.AreEqual("REG_BINARY", v.TypeLabel);
        Assert.AreEqual("01 ab", v.Data);
    }
}
