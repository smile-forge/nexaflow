using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FlaUI.Core.Definitions;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.UIJourneys.Infrastructure;
using Point = System.Drawing.Point;

namespace Nexaflow.Tests.Features.Images.UI;

/// <summary>Pictures dragged onto Artist from another application: opened on the start screen, layers after that.</summary>
[TestClass]
[CoversNode("images-layers-panel")]
public class ArtistDropJourneyTests : UiJourneyTestBase
{
    protected override string? LaunchTabKind => "Artist";

    [TestMethod]
    public void PicturesDroppedAnywhereOnArtist_BecomeLayers_InOnePass()
    {
        var start = WaitForId("Images_Start", 15);
        Assert.IsNotNull(start, "Artist's start screen did not open.");
        var r = start.BoundingRectangle;
        var from = new Point(r.Left + 70, r.Bottom - 50);
        var middle = new Point(r.Left + r.Width * 2 / 5, r.Top + r.Height / 2);

        var folder = TestTemp.CreateSubdirectory("artist-drops-").FullName;
        var first = Picture(folder, "first.png", 0x40);
        var second = Picture(folder, "second.png", 0x90);
        var third = Picture(folder, "third.png", 0xD0);

        OutsideDrag.Files(from, middle, first);
        Check("A picture dropped on the start screen opens", () => WaitForId("Image_EditCanvas", 10) is { IsOffscreen: false });

        OutsideDrag.Files(from, middle, second);
        Check("A picture dropped on the canvas is a layer over the first", () => WaitForFs(() => Layers() == 2, 10));

        if (WaitForId("Image_LayersList", 5) is { } list)
        {
            var l = list.BoundingRectangle;
            OutsideDrag.Files(from, new Point(l.Left + l.Width / 2, l.Top + 15), third);
            Check("A picture dropped on the layers is one too", () => WaitForFs(() => Layers() == 3, 10));
        }
        else Check("The layers are shown", () => false);

        Check("The pictures stay where they were", () => File.Exists(first) && File.Exists(second) && File.Exists(third));

        AssertJourney();
    }

    private int Layers() => MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("Image_LayersList"))
        ?.FindAllDescendants(cf => cf.ByControlType(ControlType.ListItem)).Length ?? 0;

    private static string Picture(string folder, string name, byte shade)
    {
        var pixels = new byte[320 * 240 * 4];
        Array.Fill(pixels, shade);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(320, 240, 96, 96, PixelFormats.Bgra32, null, pixels, 320 * 4)));

        var path = Path.Combine(folder, name);
        using var file = File.Create(path);
        encoder.Save(file);
        return path;
    }
}
