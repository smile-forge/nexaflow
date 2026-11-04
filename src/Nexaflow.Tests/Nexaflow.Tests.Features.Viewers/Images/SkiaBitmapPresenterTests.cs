
using Nexaflow.Features.Images.Editing.Rendering;
using Nexaflow.Features.Images.Editing.Wpf;
using Nexaflow.Tests.Fixtures;
using SkiaSharp;

namespace Nexaflow.Tests.Features.Images;

[TestClass]
public class SkiaBitmapPresenterTests
{
    private const int Side = 8;

    private ViewerUiThread _ui = null!;

    [TestInitialize]
    public void Make() => _ui = new ViewerUiThread();

    [TestCleanup]
    public void Clean() => _ui.Dispose();

    private static SKImage Flat(SKColor colour)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(Side, Side, SKColorType.Rgba8888, SKAlphaType.Premul, SKColorSpace.CreateSrgb()));
        bitmap.Erase(colour);
        return SKImage.FromBitmap(bitmap);
    }

    // The frame shown, once it has landed on the bitmap.
    private void Show(SkiaBitmapPresenter presenter, SKColor colour, FrameChange change)
    {
        using var landed = new ManualResetEventSlim();
        void OnPresented(object? tag, object? reading) => landed.Set();
        presenter.Presented += OnPresented;
        using var image = Flat(colour);
        presenter.Present(consume => { consume(image, change); return true; });
        Assert.IsTrue(landed.Wait(TimeSpan.FromSeconds(10)), "the frame never landed");
        presenter.Presented -= OnPresented;
    }

    private SKColor At(SkiaBitmapPresenter presenter, int x, int y) => _ui.Invoke(() =>
    {
        var pixel = new byte[4];
        presenter.Bitmap!.CopyPixels(new System.Windows.Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return new SKColor(pixel[2], pixel[1], pixel[0], pixel[3]);
    });

    [TestMethod]
    [CoversNode("images-paint")]
    public void AFrameFollowingTheOneShown_ReplacesOnlyWhatItChanged_AnyOtherIsShownWhole()
    {
        var source = new object();
        using var presenter = new SkiaBitmapPresenter(action => _ui.Run(action));
        Show(presenter, SKColors.Red, new FrameChange(source, 1, null));
        Assert.AreEqual(SKColors.Red, At(presenter, 6, 6), "the first frame is shown whole");

        Show(presenter, SKColors.Blue, new FrameChange(source, 2, new SKRectI(1, 1, 3, 3)));
        Assert.AreEqual(SKColors.Blue, At(presenter, 2, 2), "what the next frame changed is shown");
        Assert.AreEqual(SKColors.Red, At(presenter, 6, 6), "and nothing else is copied");

        Show(presenter, SKColors.Lime, new FrameChange(source, 4, new SKRectI(1, 1, 3, 3)));
        Assert.AreEqual(SKColors.Lime, At(presenter, 6, 6), "a frame that skips one is shown whole");

        var other = new object();
        Show(presenter, SKColors.Blue, new FrameChange(other, 5, new SKRectI(1, 1, 3, 3)));
        Assert.AreEqual(SKColors.Blue, At(presenter, 6, 6), "and so is one from another source");

        // Cleared, the next frame lands on a bitmap made afresh: all of what is held is written, not just what changed.
        OnUi(presenter.Clear);
        Show(presenter, SKColors.Red, new FrameChange(other, 6, new SKRectI(1, 1, 3, 3)));
        Assert.AreEqual(SKColors.Red, At(presenter, 2, 2), "what the frame changed");
        Assert.AreEqual(SKColors.Blue, At(presenter, 6, 6), "and the rest as the frame before left it");
    }

    private void OnUi(Action action) => _ui.Run(action).GetAwaiter().GetResult();
}
