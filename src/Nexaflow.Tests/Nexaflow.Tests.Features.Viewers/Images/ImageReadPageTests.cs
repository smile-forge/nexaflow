using System.IO;
using Nexaflow.Features.Common;
using Nexaflow.Features.Images.Editing;
using Nexaflow.Features.Images.Editing.Recipe;
using Nexaflow.Features.Images.Editing.Restoring;
using Nexaflow.Features.Images.ViewModels;
using Nexaflow.Tests.Fixtures;
using NSubstitute;
using SkiaSharp;
using static Nexaflow.Tests.Features.Images.EditingFixtures;

namespace Nexaflow.Tests.Features.Images;

/// <summary>
/// Read page in the viewer: offered for a picture the text finder takes for a page — looked at once the picture is there
/// to look at, however long it takes to arrive — and not for one it does not.
/// </summary>
[TestClass]
[CoversNode("images-read-page")]
public class ImageReadPageTests
{
    private string _dir = "";
    private string _picture = "";
    private ViewerUiThread _ui = null!;

    [TestInitialize]
    public void Make()
    {
        _dir = TestTemp.CreateSubdirectory("nexaflow-read-page-").FullName;
        _picture = Path.Combine(_dir, "letter.png");
        using (var bitmap = Flat(48, 64, SKColors.White)) File.WriteAllBytes(_picture, EncodePng(bitmap));
        _ui = new ViewerUiThread();
    }

    [TestCleanup]
    public void Clean()
    {
        _ui.Dispose();
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private IShellServices Shell()
    {
        var shell = Substitute.For<IShellServices>();
        shell.RunOnUiAsync(Arg.Any<Action>()).Returns(call => _ui.Run(call.Arg<Action>()));
        return shell;
    }

    private static IPageReading Finding(bool page)
    {
        var reading = Substitute.For<IPageReading>();
        reading.State.Returns(ToolModelsState.Ready);
        reading.ModelsVersion.Returns("test-1");
        reading.FindTextAsync(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
               .Returns(new TextOnPicture([], page));
        return reading;
    }

    private ImageViewModel Viewing(IPageReading reading)
        => _ui.Invoke(() => new ImageViewModel([_picture], Shell(), ImageViewMode.Carousel,
                                               new ImageEditingServices(new ImageRecipeStore(TimeSpan.FromHours(1)),
                                                                        new RestoredPictures(Path.Combine(_dir, "restored"), RestoredPictures.DefaultBudget)),
                                               reading: reading));

    /// <summary>Waits — on the viewer's UI thread, where it is read — until <paramref name="done"/>.</summary>
    private bool Until(Func<bool> done)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(10))
        {
            if (_ui.Invoke(done)) return true;
            Thread.Sleep(10);
        }
        return false;
    }

    [TestMethod]
    public void APictureOfAPage_IsOfferedToBeRead_OnceItHasArrived()
    {
        var vm = Viewing(Finding(page: true));

        Assert.IsTrue(Until(() => vm.ReadPage.CanRead), "the picture is looked at when it is decoded, not only when it is opened");
    }

    [TestMethod]
    public void APictureThatIsNoPage_IsNotOffered()
    {
        var reading = Finding(page: false);
        var vm = Viewing(reading);

        Assert.IsTrue(Until(() => reading.ReceivedCalls().Any(c => c.GetMethodInfo().Name == nameof(IPageReading.FindTextAsync))),
                      "the picture is looked at");
        Assert.IsFalse(_ui.Invoke(() => vm.ReadPage.CanRead));
    }
}
