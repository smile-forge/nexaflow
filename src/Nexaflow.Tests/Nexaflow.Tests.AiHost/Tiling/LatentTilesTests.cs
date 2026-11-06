using Nexaflow.AiHost.Tiling;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.AiHost.Tiling;

/// <summary>A latent plane cut into overlapping tiles, and what they give back blended into the whole.</summary>
[TestClass]
[CoversNode("owned-images")]
public class LatentTilesTests
{
    [TestMethod]
    public void APlane_IsCutIntoTilesOfOneSize_ThatOverlapTheirNeighbours_TheLastOfARowEndingAtItsEdge()
    {
        var grid = LatentTiles.Over(width: 100, height: 64, side: 40, overlap: 8);

        Assert.IsTrue(grid.Tiles.All(t => t is { Width: 40, Height: 40 }), "one size throughout");
        CollectionAssert.AreEqual(new[] { 0, 32, 60 }, grid.Tiles.Select(t => t.X).Distinct().ToArray(), "a step of side less overlap, the last moved back to end at 100");
        CollectionAssert.AreEqual(new[] { 0, 24 }, grid.Tiles.Select(t => t.Y).Distinct().ToArray());
        Assert.AreEqual(6, grid.Tiles.Count, "row by row");
        Assert.AreEqual((0, 0), (grid.Tiles[0].X, grid.Tiles[0].Y));
        Assert.AreEqual((60, 24), (grid.Tiles[^1].X, grid.Tiles[^1].Y));
    }

    [TestMethod]
    public void APlaneNoLargerThanATile_IsOneTile()
    {
        var grid = LatentTiles.Over(width: 30, height: 20, side: 40, overlap: 8);

        Assert.AreEqual(1, grid.Tiles.Count);
        Assert.AreEqual(new LatentTile(0, 0, 30, 20), grid.Tiles[0]);
    }

    [TestMethod]
    public void AnOverlapOfHalfATileOrMore_IsRefused()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => LatentTiles.Over(100, 100, side: 40, overlap: 20));
    }

    [TestMethod]
    public void TilesThatAgree_BlendIntoExactlyWhatEachGave_AtTheScaleTheyGiveItBackAt()
    {
        const int scale = 8, channels = 3;
        var grid = LatentTiles.Over(width: 50, height: 30, side: 16, overlap: 4);
        var blend = new TileBlend(grid, channels, scale);
        static float Field(int c, int x, int y) => c * 1000 + y * 0.5f + x * 0.25f;

        foreach (var t in grid.Tiles)
        {
            int w = t.Width * scale, h = t.Height * scale;
            var made = new float[channels * w * h];
            for (int c = 0; c < channels; c++)
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                        made[c * w * h + y * w + x] = Field(c, t.X * scale + x, t.Y * scale + y);
            blend.Add(t, made);
        }

        var result = blend.Result();
        Assert.AreEqual((400, 240), (blend.Width, blend.Height));
        int whole = blend.Width * blend.Height;
        for (int c = 0; c < channels; c++)
            for (int y = 0; y < blend.Height; y++)
                for (int x = 0; x < blend.Width; x++)
                    Assert.AreEqual(Field(c, x, y), result[c * whole + y * blend.Width + x], 1e-2f, $"({c},{x},{y}): the weights at every value sum to one");
    }

    [TestMethod]
    public void TilesThatDisagree_HandOverGraduallyAcrossTheCellsTheyShare_AndKeepTheirOwnValuesOutsideThem()
    {
        var grid = LatentTiles.Over(width: 28, height: 8, side: 16, overlap: 4);
        Assert.AreEqual(2, grid.Tiles.Count);
        var blend = new TileBlend(grid, channels: 1, scale: 1);
        blend.Add(grid.Tiles[0], Enumerable.Repeat(0f, 16 * 8).ToArray());
        blend.Add(grid.Tiles[1], Enumerable.Repeat(1f, 16 * 8).ToArray());

        var row = blend.Result().AsSpan(0, 28).ToArray();
        Assert.IsTrue(row[..12].All(v => v == 0), "the first tile's own cells");
        Assert.IsTrue(row[16..].All(v => v == 1), "the second's");
        var seam = row[12..16];
        Assert.IsTrue(seam.Zip(seam.Skip(1)).All(p => p.First < p.Second) && seam[0] > 0 && seam[^1] < 1, "rising from one to the other");
    }

    [TestMethod]
    public void ATileGivingBackTheWrongNumberOfValues_IsRefused()
    {
        var grid = LatentTiles.Over(16, 16, side: 16, overlap: 4);
        var blend = new TileBlend(grid, channels: 3, scale: 8);
        Assert.ThrowsExactly<ArgumentException>(() => blend.Add(grid.Tiles[0], new float[3 * 128 * 127]));
    }
}
