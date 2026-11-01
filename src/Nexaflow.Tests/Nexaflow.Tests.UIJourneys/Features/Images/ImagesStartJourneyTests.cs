using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.UIJourneys.Infrastructure;

namespace Nexaflow.Tests.Features.Images.UI;

/// <summary>
/// New picture, the user driving: an Images tab opened with no picture shows it — a blank canvas set up and created, the
/// clipboard's picture pasted as a canvas of its own, and a canvas opened here before opened again — each turning the tab into
/// the canvas, in the editor with its layers. The workspace here draws no pictures, so the start screen says so and offers its
/// AI settings; drawing into a canvas is <see cref="ImagesCanvasGenerateJourneyTests"/>'s.
/// Interactive desktop only — run with <c>--filter "TestCategory=UI"</c>.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("images-start")]
public class ImagesStartJourneyTests : ImageEditJourneyBase
{
    protected override string? LaunchTabKind => "Images";

    private string _folder = "";
    private string _recent = "";

    /// <summary>A canvas opened here before, remembered by the Images settings, and a picture on the clipboard.</summary>
    protected override void SeedConfig(string configDir)
    {
        _folder = Path.Combine(TestTemp.Root, "nexaflow-start-journey-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
        _recent = Path.Combine(_folder, "poster.ora");
        WriteCanvas(_recent);

        var version = FileVersionInfo.GetVersionInfo(BuiltOutput.App).FileVersion ?? "1.0.0.0";
        var images = Path.Combine(configDir, "images");
        Directory.CreateDirectory(images);
        File.WriteAllText(Path.Combine(images, $"config_{version}.json"), $$"""
            {
              "ConfigName": "images",
              "HistoryMegabytes": 1024,
              "RecentCanvases": [ {{JsonSerializer.Serialize(_recent)}} ]
            }
            """);

        PutPictureOnTheClipboard();
    }

    [TestCleanup]
    public void RemoveFolder()
    {
        try { if (Directory.Exists(_folder)) Directory.Delete(_folder, true); } catch { }
    }

    [TestMethod]
    public void ABlankCanvas_SetUpAndCreated_OpensInTheEditor()
    {
        CheckPresent("An Images tab with no picture shows New picture", "Images_Start", 15);
        CheckPresent("Its canvas is set up as New's is", "Images_CanvasSetup");
        CheckPresent("No model draws here: the workspace's AI settings are offered", "Images_StartAiSettings");
        CheckPresent("Pictures can be opened from disk", "Images_StartOpen");
        Check("Print is there, with nothing to print", () => Exists("Image_Print") && !Enabled("Image_Print"));
        Check("A size typed by hand", () => TypeInto("Images_CanvasWidth", "320") && TypeInto("Images_CanvasHeight", "200"));
        CheckDoes("Create opens the canvas in the editor", "Images_StartCreate",
                  () => WaitForFs(() => Exists("Image_LayersPanel"), 15) && WaitForGone("Images_Start", 3));

        AssertJourney();
    }

    [TestMethod]
    public void TheClipboardsPicture_PastedAsACanvas_OpensInTheEditor()
    {
        CheckPresent("New picture offers what the clipboard holds", "Images_StartPaste", 15);
        CheckDoes("Paste opens it as a canvas of its own", "Images_StartPaste", () => WaitForFs(() => Exists("Image_LayersPanel"), 15));

        AssertJourney();
    }

    [TestMethod]
    public void ACanvasOpenedBefore_IsOpenedAgainFromTheList()
    {
        CheckPresent("Canvases opened before are listed", "Images_StartRecentList", 15);
        CheckPresent("The canvas opened before is among them", "Images_StartRecent");
        CheckDoes("Clicked, it opens in the editor", "Images_StartRecent", () => WaitForFs(() => Exists("Image_LayersPanel"), 15));

        AssertJourney();
    }

    /// <summary>A small OpenRaster canvas: one layer and its merged image.</summary>
    private static void WriteCanvas(string path)
    {
        var png = File.ReadAllBytes(TestSampleData.Files("images").First(f => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase)));
        int width  = png[16] << 24 | png[17] << 16 | png[18] << 8 | png[19];
        int height = png[20] << 24 | png[21] << 16 | png[22] << 8 | png[23];
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        using (var mime = new StreamWriter(zip.CreateEntry("mimetype", CompressionLevel.NoCompression).Open(), new UTF8Encoding(false)))
            mime.Write("image/openraster");
        using (var xml = new StreamWriter(zip.CreateEntry("stack.xml").Open(), new UTF8Encoding(false)))
            xml.Write($"""
                <?xml version="1.0" encoding="UTF-8"?>
                <image version="0.0.6" w="{width}" h="{height}">
                 <stack>
                  <layer src="data/base.png" x="0" y="0" name="Base"/>
                 </stack>
                </image>
                """);
        using (var layer = zip.CreateEntry("data/base.png").Open()) layer.Write(png);
        using (var merged = zip.CreateEntry("mergedimage.png").Open()) merged.Write(png);
    }

    /// <summary>A small picture on the clipboard, as another application puts one there.</summary>
    private static void PutPictureOnTheClipboard()
    {
        var thread = new Thread(() =>
        {
            var pixels = Enumerable.Repeat((byte)180, 8 * 6 * 4).ToArray();
            Clipboard.SetImage(BitmapSource.Create(8, 6, 96, 96, PixelFormats.Bgra32, null, pixels, 8 * 4));
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
    }
}
