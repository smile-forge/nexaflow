using System.IO;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.VirtualDisk;

/// <summary>
/// Builds the disk image the VirtualDisk UI journey double-clicks. This is the fixture that genuinely needs
/// a product library to write — DiscUtils, the same one the feature reads it with — so it is built by the
/// suite that already references it, and the journey only opens the result.
/// </summary>
[TestClass]
[NoCoverage("Builds a fixture for another suite; asserts no behaviour of its own.")]
public class DiskUiFixtureTests
{
    [TestMethod]
    public void SeedsTheDiskImageForTheUiJourney()
    {
        Directory.CreateDirectory(UiFixtures.DiskFolder);
        DiskSampleFactory.CreateFatVhd(UiFixtures.DiskFolder, "sample.vhd");
    }
}
