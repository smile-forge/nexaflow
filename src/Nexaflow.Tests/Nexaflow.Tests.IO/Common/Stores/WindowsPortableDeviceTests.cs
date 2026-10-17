using Nexaflow.IO.Common.Portable;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Common.Stores;

/// <summary>
/// The MTP store against whatever phone or camera this machine has plugged in, reading only: it opens, names itself,
/// says what storage it shares and lists the top of it. Inconclusive with none plugged in; a locked phone shares no
/// storage, and that is an answer too.
/// </summary>
[TestClass]
[CoversNode("io-file-stores")]
public class WindowsPortableDeviceTests
{
    [TestMethod]
    public async Task APluggedInPhone_OpensAndSaysWhatItShares()
    {
        var devices = WpdStore.DevicePaths()
            .Where(p => p.StartsWith(@"\\?\usb#", StringComparison.OrdinalIgnoreCase)).ToList();
        if (devices.Count == 0) Assert.Inconclusive("no phone or camera is plugged in over USB");

        using var store = WpdStore.Open(devices[0]);
        Assert.IsFalse(string.IsNullOrEmpty(store.Name));
        var roots = await store.RootsAsync(CancellationToken.None);
        foreach (var root in roots)
        {
            Assert.IsFalse(string.IsNullOrEmpty(root.Name));
            var entries = await store.ListAsync(root.Id, CancellationToken.None);
            Assert.IsTrue(entries.All(e => e.Name.Length > 0), "every entry has a name");
        }
    }
}
