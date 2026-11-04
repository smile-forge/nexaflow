using Nexaflow.Features.Images.Editing.SceneDepth;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Images;

[TestClass]
public class DepthSubjectTests
{
    private const int Wide = 48, High = 36;

    // A far background, with (left, top, right, bottom) squares raised to their nearness.
    private static SceneDepthPlane Plane(params (int Left, int Top, int Right, int Bottom, float Nearness)[] raised)
    {
        var nearness = new float[Wide * High];
        Array.Fill(nearness, 0.1f);
        foreach (var (left, top, right, bottom, near) in raised)
            for (int y = top; y < bottom; y++)
                for (int x = left; x < right; x++) nearness[y * Wide + x] = near;
        return new SceneDepthPlane(nearness, Wide, High);
    }

    [TestMethod]
    [CoversNode("images-selection-subject")]
    public void TheSubject_IsTheNearThingTowardTheMiddle_NotANearerSpeckInACorner_BoxedInThePhotosPixels()
    {
        var plane = Plane((18, 10, 30, 26, 0.8f), (0, 0, 3, 3, 0.95f));
        var box = DepthSubject.Box(plane, Wide * 10, High * 10);
        Assert.IsNotNull(box, "something stands out");
        Assert.IsTrue(box.Left < 180 && box.Left > 120 && box.Right > 300 && box.Right < 360, $"boxed round the thing across: {box}");
        Assert.IsTrue(box.Top < 100 && box.Top > 40 && box.Bottom > 260 && box.Bottom < 320, $"and down: {box}");
    }

    [TestMethod]
    [CoversNode("images-selection-subject")]
    public void AFlatDepth_HasNoSubject()
    {
        Assert.IsNull(DepthSubject.Box(Plane(), Wide, High), "all of it at one distance, nothing stands out");
    }
}
