using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Restoring;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.AiHost.Restoring;

/// <summary>
/// The arithmetic around the restoration graphs, without them: how a picture is cut into tiles, how RetinaFace's priors
/// are laid and its regressions read, and which parts a restoration needs.
/// </summary>
[TestClass]
[CoversNode("picture-tools")]
public class RestoringTests
{
    [TestMethod]
    public void APicture_IsCutIntoTiles_EachReadingAMarginOfItsNeighbours_AndKeepingItsOwnSquare()
    {
        var tiles = PictureUpscaling.Tiles(1100, 600, 512);

        Assert.AreEqual(6, tiles.Count, "three across, two down");
        Assert.AreEqual(new PictureUpscaling.TileArea(0, 0, 536, 536, 0, 0, 512, 512), tiles[0]);
        Assert.AreEqual(new PictureUpscaling.TileArea(488, 0, 560, 536, 512, 0, 512, 512), tiles[1], "a margin each side within the picture");
        Assert.AreEqual(new PictureUpscaling.TileArea(1000, 488, 100, 112, 1024, 512, 76, 88), tiles[5], "the last, cut short by the edge");
        Assert.AreEqual(1100L * 600, tiles.Sum(t => (long)t.KeepWidth * t.KeepHeight), "the kept squares cover the picture once");
    }

    [TestMethod]
    public void The4xUpscaler_TakesSmallerTiles_ThanThe2x()
    {
        Assert.AreEqual(512, PictureUpscaling.TileSide(2));
        Assert.AreEqual(256, PictureUpscaling.TileSide(4), "it works at the tile's full size, so a smaller tile keeps it under 3 GB");
        Assert.AreEqual((1, 0), PictureUpscaling.ModPad(1101, 600, 2), "the 2x model takes even sides");
        Assert.AreEqual((0, 0), PictureUpscaling.ModPad(1101, 601, 4));
    }

    [TestMethod]
    public void Priors_AreTwoAnchorsAPlace_OverThreeStrides_InTheNetworksOrder()
    {
        var priors = RetinaFaceDecoding.Priors(64, 48);

        Assert.AreEqual(4 * (8 * 6 * 2 + 4 * 3 * 2 + 2 * 2 * 2), priors.Length, "steps of 8, 16 and 32, each place two sizes");
        CollectionAssert.AreEqual(new[] { 4 / 64f, 4 / 48f, 16 / 64f, 16 / 48f }, priors[..4], "the first, at the first place, the smaller size");
        CollectionAssert.AreEqual(new[] { 4 / 64f, 4 / 48f, 32 / 64f, 32 / 48f }, priors[4..8], "then the larger, at the same place");
    }

    [TestMethod]
    public void AFace_IsReadAgainstItsPrior_TheSurestKept_AndOneOverlappingIt_Dropped()
    {
        int width = 64, height = 64;
        int count = RetinaFaceDecoding.Priors(width, height).Length / 4;
        var boxes = new float[count * 4];
        var scores = new float[count];
        var landmarks = new float[count * 10];
        scores[0] = 0.99f;     // at (4, 4), 16 across, unmoved
        scores[2] = 0.98f;     // the next place along, (12, 4), regressed back onto the first: the same face
        boxes[2 * 4] = -5f;
        scores[100] = 0.5f;    // not sure enough
        landmarks[0] = 1f;     // its first point a tenth of the anchor right of the centre

        var faces = RetinaFaceDecoding.Decode(boxes, scores, landmarks, width, height);

        Assert.AreEqual(1, faces.Count);
        var face = faces[0];
        Assert.AreEqual((-4f, -4f, 12f, 12f, 0.99f), (face.Left, face.Top, face.Right, face.Bottom, face.Score));
        Assert.AreEqual(4 + 1.6, face.Points[0].X, 1e-5, "regressed by a tenth of the 16-pixel anchor");
        Assert.AreEqual(4, face.Points[1].X, 1e-5);
    }

    [TestMethod]
    [DataRow(1, false, new[] { RestoringParts.UpscalerX2 })]
    [DataRow(4, false, new[] { RestoringParts.UpscalerX4 })]
    [DataRow(2, true, new[] { RestoringParts.UpscalerX2, RestoringParts.FaceDetector, RestoringParts.FaceRestorer, RestoringParts.FaceParser })]
    public void ARestoration_UsesTheUpscalerForItsScale_AndTheFacePartsOnlyWhereAsked(int scale, bool faces, string[] parts)
        => CollectionAssert.AreEqual(parts, RestoringParts.Of(scale, faces).ToArray());

    [TestMethod]
    public void TheCropAround_AFace_IsAlignedToGfpgansTemplate()
    {
        var face = new FoundFace(0, 0, 0, 0, 1, [.. FaceAlignment.Template.Select(p => (p.X / 2 + 10, p.Y / 2 + 20))]);

        var alignment = FaceAlignment.Alignment(face);

        var (x, y) = alignment.Map(face.Points[2].X, face.Points[2].Y);
        Assert.AreEqual(FaceAlignment.Template[2].X, x, 1e-6, "a face half the template's size, moved, maps onto it");
        Assert.AreEqual(FaceAlignment.Template[2].Y, y, 1e-6);
        var corner = FaceAlignment.PasteBack(alignment, 2).Map(0, 0);
        Assert.AreEqual(10 * 2 + 1, corner.X, 1e-6, "the template's corner lands on the face's, on a picture twice the size, half a scaled pixel on");
        Assert.AreEqual(20 * 2 + 1, corner.Y, 1e-6);
    }
}
