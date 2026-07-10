using Microsoft.Win32;
using Nexaflow.Features.WindowsRegistry.Services;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.WindowsRegistry;

/// <summary>
/// Round-trips the in-process write path (<see cref="RegistryWriter"/>) together with the codec's wire
/// decoding. Every test works under a private, per-test <c>HKCU\Software\Nexaflow.Tests\{guid}</c> subtree
/// — no elevation, no HKLM/HKCR, and the subtree is deleted in cleanup. (Live elevated writes are out of scope.)
/// </summary>
[TestClass]
[CoversNode("registry-write-elevation")]
public class RegistryWriterTests
{
    private static readonly RegistryRoot Root = RegistryRoot.CurrentUser;
    private string _sub = "";

    [TestInitialize]
    public void Setup() => _sub = $@"Software\Nexaflow.Tests\{Guid.NewGuid():N}";

    [TestCleanup]
    public void Teardown()
    {
        try { Registry.CurrentUser.DeleteSubKeyTree(_sub, throwOnMissingSubKey: false); } catch { }
    }

    private RegistryKey? Open(string sub) => Registry.CurrentUser.OpenSubKey(sub, writable: false);

    [TestMethod]
    public void CreateKey_ThenKeyExists()
    {
        RegistryWriter.CreateKey(Root, _sub);
        using var key = Open(_sub);
        Assert.IsNotNull(key);
    }

    [TestMethod]
    public void SetValue_Dword_WritesDataAndKind()
    {
        RegistryWriter.CreateKey(Root, _sub);
        RegistryWriter.SetValue(Root, _sub, "Flags", "DWord", RegistryValueCodec.Encode(26, RegistryValueKind.DWord));

        using var key = Open(_sub)!;
        Assert.AreEqual(26, key.GetValue("Flags"));
        Assert.AreEqual(RegistryValueKind.DWord, key.GetValueKind("Flags"));
    }

    [TestMethod]
    public void SetValue_MultiString_RoundTripsThroughWire()
    {
        RegistryWriter.CreateKey(Root, _sub);
        var items = new[] { "a", "b", "c" };
        RegistryWriter.SetValue(Root, _sub, "List", "MultiString", RegistryValueCodec.Encode(items, RegistryValueKind.MultiString));

        using var key = Open(_sub)!;
        CollectionAssert.AreEqual(items, (string[])key.GetValue("List")!);
        Assert.AreEqual(RegistryValueKind.MultiString, key.GetValueKind("List"));
    }

    [TestMethod]
    public void DeleteValue_RemovesIt()
    {
        RegistryWriter.CreateKey(Root, _sub);
        RegistryWriter.SetValue(Root, _sub, "Temp", "String", "x");
        RegistryWriter.DeleteValue(Root, _sub, "Temp");

        using var key = Open(_sub)!;
        Assert.IsNull(key.GetValue("Temp"));
    }

    [TestMethod]
    public void DeleteKey_RemovesTheSubtree()
    {
        RegistryWriter.CreateKey(Root, _sub);
        RegistryWriter.DeleteKey(Root, _sub);
        Assert.IsNull(Open(_sub));
    }

    [TestMethod]
    public void RenameKey_MovesValues_AndRemovesOldKey()
    {
        // Work on a child of the per-test subtree so the fixed "Renamed" name can't collide
        // with parallel runs and cleanup of _sub still covers it.
        var node = $@"{_sub}\Node";
        RegistryWriter.CreateKey(Root, node);
        RegistryWriter.SetValue(Root, node, "Keep", "String", "value");

        RegistryWriter.RenameKey(Root, node, "Renamed");

        Assert.IsNull(Open(node), "old key should be gone");
        using var renamed = Open($@"{_sub}\Renamed");
        Assert.IsNotNull(renamed);
        Assert.AreEqual("value", renamed!.GetValue("Keep"));
    }

    [TestMethod]
    public void DeleteKey_OnHiveRoot_Throws()
        => Assert.ThrowsExactly<InvalidOperationException>(() => RegistryWriter.DeleteKey(Root, ""));
}
