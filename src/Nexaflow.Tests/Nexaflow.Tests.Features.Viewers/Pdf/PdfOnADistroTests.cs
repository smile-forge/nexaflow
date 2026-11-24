using System.Diagnostics;
using System.IO;
using Nexaflow.Features.Pdf.Editing;
using Nexaflow.IO.Common;
using Nexaflow.IO.Common.Locations;
using Nexaflow.IO.Location.Wsl;
using Nexaflow.Tests.Fixtures;
using UglyToad.PdfPig;

namespace Nexaflow.Tests.Features.Pdf;

/// <summary>
/// A PDF in a real WSL distro, edited and saved: the save lands, and the file keeps what Linux keeps — its permissions,
/// owner and group. The file is the test's own, under a folder it makes in the distro's <c>/tmp</c> and removes.
/// </summary>
[TestClass]
[DoNotParallelize]
[TestCategory("Wsl")]
[CoversNode("pdf-save")]
public sealed class PdfOnADistroTests
{
    private sealed class NoSecrets : ISecretStore
    {
        public string? Read(string target) => null;
        public void Write(string target, string? userName, string secret) { }
        public bool Delete(string target) => false;
    }

    [TestMethod]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task ASavedEdit_KeepsTheFilesPermissions()
    {
        var provider = new WslLocationProvider();
        var distros  = await provider.DiscoverAsync(CancellationToken.None);
        if (distros.Count == 0) Assert.Inconclusive("WSL has no distro installed on this machine.");

        var registry = new LocationProviderRegistry();
        registry.Register(provider);
        await using var pool = new LocationSessionPool(registry, new StoredCredentialSource(new NoSecrets()));
        var set   = pool.Acquire([distros[0]]);
        var files = new LocationFileSystem(VirtualFileSystem.Instance);
        files.RegisterLocation(set.Mounts[0]);

        var name   = $"nexaflow-pdf-save-{Guid.NewGuid():N}";
        var folder = $@"{set.Mounts[0].Root}\tmp\{name}";
        var pdf    = $@"{folder}\outline.pdf";
        await files.CreateDirectoryAsync(folder);
        try
        {
            await files.WriteAllBytesAsync(pdf, await File.ReadAllBytesAsync(TestSampleData.Path("pdf", "outline.pdf")));
            await InDistroAsync(distros[0].Host, "chmod", "640", $"/tmp/{name}/outline.pdf");
            var before = (await files.GetEntryAsync(pdf))!;

            using (var session = new PdfDocumentSession(pdf, files))
            {
                Assert.IsTrue(await session.OpenAsync(CancellationToken.None));
                session.Move([2], 0);
                await session.SaveAsync(null, CancellationToken.None);
            }

            var after = (await files.GetEntryAsync(pdf))!;
            using (var saved = PdfDocument.Open(await files.ReadAllBytesAsync(pdf)))
                Assert.AreEqual("third page", saved.GetPage(1).Text, "the edit was saved");
            Assert.AreEqual(before.Mode, after.Mode, "rw-r----- stays rw-r-----");
            Assert.AreEqual(before.Owner, after.Owner);
            Assert.AreEqual(before.Group, after.Group);
        }
        finally
        {
            await files.DeleteAsync(folder, recursive: true);
            pool.Release(set);
        }
    }

    private static async Task InDistroAsync(string distro, params string[] arguments)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "wsl.exe")) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in (string[])["-d", distro, "--exec", .. arguments]) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        await process.WaitForExitAsync();
        Assert.AreEqual(0, process.ExitCode, string.Join(' ', arguments));
    }
}
