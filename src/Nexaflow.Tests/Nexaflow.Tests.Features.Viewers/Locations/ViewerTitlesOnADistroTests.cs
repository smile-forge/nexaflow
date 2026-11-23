using System.Collections.Generic;
using Nexaflow.Features.Common;
using Nexaflow.Features.Hex;
using Nexaflow.Features.Text;
using Nexaflow.IO.Common;
using Nexaflow.IO.Common.Locations;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Features.Viewers.Locations;

/// <summary>
/// A file in a WSL distro opens in a viewer titled by its Linux name — <c>at 12:30.txt</c>, not the segment the file
/// system encodes it as — and its leaf crumb keeps that name.
/// </summary>
[TestClass]
[CoversNode("locations-viewers")]
public sealed class ViewerTitlesOnADistroTests
{
    private const string File = @"::wsl-titleprobe\home\at 12%3A30.txt";

    private static LocationFileSystem Files()
    {
        var files = new LocationFileSystem(VirtualFileSystem.Instance);
        files.RegisterLocation(new LocationMount(new ConnectionProfile("wsl-titleprobe", "wsl", "Titleprobe", "Titleprobe"),
                                                 LocationCapabilities.SameMachine, null!));
        return files;
    }

    private static Dictionary<string, string> At(string path) => new() { ["path"] = path };

    [TestMethod]
    public void AViewersTab_IsTitledByTheFilesLinuxName()
    {
        var files = Files();
        var shell = Substitute.For<IShellServices>();

        foreach (var page in new[]
                 {
                     new TextTabRegistration(shell, files).CreatePageDefinition(At(File)),
                     new HexTabRegistration(shell, files).CreatePageDefinition(At(File)),
                 })
        {
            Assert.AreEqual("at 12:30.txt", page.Title);
            Assert.AreEqual("at 12:30.txt", page.Breadcrumbs[^1].Label, "the leaf crumb is named as the tab is");
        }
    }
}
