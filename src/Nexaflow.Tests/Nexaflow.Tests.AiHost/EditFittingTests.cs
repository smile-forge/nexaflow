using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Drawing;
using Nexaflow.AiHost;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.AiHost;

/// <summary>An edit's pictures sent as they are and read as its model reads them: each brought to its model's size for its
/// proportions, the edit drawn at the first one's, and the mask over the first brought along with it.</summary>
[TestClass]
[CoversNode("owned-images")]
public class EditFittingTests
{
    private static readonly DrawingModel Model = new(DrawingMethod.Flux2Klein, new Dictionary<string, ModelSpec>(), 4, 0f, 0f);

    private static Draw Edit(params DrawingSource[] sources) => new(1, Model, "a lake", 0, 0, 1, RequestPriority.Job) { Sources = sources };

    private static DrawingSource Picture(int width, int height) => new(width, height, new byte[width * height * 3]);

    [TestMethod]
    public void EachPicture_IsReadAtItsModelsSize_AndTheEditDrawnAtTheFirstOnes()
    {
        var fitted = EditFitting.Fitted(Edit(Picture(2000, 1000), Picture(300, 400)), KleinSampler.ReadSize);

        Assert.AreEqual((1440, 720), (fitted.Width, fitted.Height));
        Assert.AreEqual((1440, 720, 1440 * 720 * 3), (fitted.Sources[0].Width, fitted.Sources[0].Height, fitted.Sources[0].Rgb.Length));
        Assert.AreEqual((288, 400), (fitted.Sources[1].Width, fitted.Sources[1].Height), "a small picture is never scaled up, only cut to whole tokens");
    }

    [TestMethod]
    public void APictureAlreadyAtItsModelsSize_IsReadAsItCame()
    {
        var source = Picture(1024, 768);

        var fitted = EditFitting.Fitted(Edit(source), KleinSampler.ReadSize);

        Assert.AreSame(source, fitted.Sources[0]);
        Assert.AreEqual((1024, 768), (fitted.Width, fitted.Height));
    }

    [TestMethod]
    public void TheMask_IsBroughtToTheSizeTheEditIsDrawnAt()
    {
        var mask = new byte[2000 * 1000];
        for (int y = 0; y < 1000; y++) mask.AsSpan(y * 2000, 1000).Fill(255);   // the left half drawn again

        var fitted = EditFitting.Fitted(Edit(Picture(2000, 1000)) with { Mask = mask }, KleinSampler.ReadSize);

        Assert.AreEqual(1440 * 720, fitted.Mask!.Length);
        Assert.AreEqual(255, fitted.Mask[360 * 1440 + 100], "where it is drawn again");
        Assert.AreEqual(0, fitted.Mask[360 * 1440 + 1300], "where the picture is kept");
    }

    [TestMethod]
    public void APictureOrMask_NotTheSizeItSays_IsRefused()
    {
        var shortPicture = new DrawingSource(10, 10, new byte[299]);
        Assert.ThrowsExactly<HostRequestException>(() => EditFitting.Fitted(Edit(shortPicture), KleinSampler.ReadSize));
        Assert.ThrowsExactly<HostRequestException>(() => EditFitting.Fitted(Edit(Picture(64, 64)) with { Mask = new byte[63 * 64] }, KleinSampler.ReadSize));
    }
}
