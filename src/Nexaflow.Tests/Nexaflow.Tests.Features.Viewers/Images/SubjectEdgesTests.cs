using Nexaflow.Features.Images.Editing.Recipe;
using Nexaflow.Features.Images.Editing.SceneDepth;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Images;

/// <summary>
/// A lens blur's edge at the subject it is focused on: the depth, which runs across the subject's outline over several pixels
/// as the plane is soft and coarse, set right there from each side's own pixels farther in — so the subject's rim stays as
/// sharp as the subject and what lies just behind it is blurred as the rest of the background is, with no halo.
/// </summary>
[TestClass]
[CoversNode("images-scene-depth-edges")]
public class SubjectEdgesTests
{
    private const int Width = 64, Height = 16, Edge = 32, Bleed = 4;
    private const float Subject = 0.9f, Behind = 0.2f;

    /// <summary>A subject covering the columns left of <see cref="Edge"/>, at <see cref="Subject"/>'s nearness, before a
    /// background at <see cref="Behind"/>'s — the depth running from one to the other over <see cref="Bleed"/> pixels each side
    /// of the outline, as the plane's does.</summary>
    private static (float[] Nearness, float[] Covered) Scene()
    {
        var nearness = new float[Width * Height];
        var covered = new float[Width * Height];
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                float t = Math.Clamp((x - (Edge - Bleed) + 0.5f) / (2 * Bleed), 0, 1);
                nearness[y * Width + x] = Subject + (Behind - Subject) * t;
                covered[y * Width + x] = x < Edge ? 1 : 0;
            }
        return (nearness, covered);
    }

    [TestMethod]
    public void AtTheOutline_EachSideTakesItsOwnDepth_AndFartherOutTheDepthIsAsItWas()
    {
        var (nearness, covered) = Scene();
        var before = (float[])nearness.Clone();
        SubjectEdges.Refine(nearness, covered, Width, Height, Bleed);

        int row = Height / 2 * Width;
        for (int x = Edge - Bleed; x < Edge; x++)
            Assert.AreEqual(Subject, nearness[row + x], 1e-4, $"column {x}, the subject's rim, is as near as the subject");
        for (int x = Edge; x < Edge + Bleed; x++)
            Assert.AreEqual(Behind, nearness[row + x], 1e-4, $"column {x}, just behind its outline, is as far as the background");
        for (int x = 0; x < Edge - 2 * Bleed; x++)
            Assert.AreEqual(before[row + x], nearness[row + x], "well inside, the plane's own depth is kept");
        for (int x = Edge + 2 * Bleed; x < Width; x++)
            Assert.AreEqual(before[row + x], nearness[row + x], "and well outside");
    }

    [TestMethod]
    public void TheOutlinesSoftPixels_AreSharedBetweenTheTwoSides()
    {
        var (nearness, covered) = Scene();
        for (int y = 0; y < Height; y++) covered[y * Width + Edge - 1] = 0.5f;
        SubjectEdges.Refine(nearness, covered, Width, Height, Bleed);
        Assert.AreEqual((Subject + Behind) / 2, nearness[Height / 2 * Width + Edge - 1], 1e-3, "half covered, half way between the two depths");
    }

    [TestMethod]
    public void AStrandTooThinToHoldItsOwnDepth_TakesTheSubjectsAsAWhole()
    {
        const int strand = 50;
        var (nearness, covered) = Scene();
        for (int y = 0; y < Height; y++)
        {
            covered[y * Width + strand] = 1;
            nearness[y * Width + strand] = 0.4f;   // the plane too coarse to see it: mostly the background's depth
        }
        SubjectEdges.Refine(nearness, covered, Width, Height, Bleed);
        Assert.AreEqual(Subject, nearness[Height / 2 * Width + strand], 1e-3, "a strand with no depth of its own is as near as its subject");
    }

    [TestMethod]
    public void ABlurFocusedOnTheSubject_LeavesItsRimSharp_AndBlursWhatLiesJustBehindIt()
    {
        // Fine stripes, so blur shows as stripes evened out: soft ones on the subject, strong ones behind it.
        var rgba = new float[Width * Height * 4];
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                float value = x < Edge ? (x % 2 == 0 ? 0.4f : 0.6f) : (x % 2 == 0 ? 1f : 0f);
                for (int c = 0; c < 3; c++) rgba[(y * Width + x) * 4 + c] = value;
                rgba[(y * Width + x) * 4 + 3] = 1;
            }

        float[] Blurred(bool setRight, float sharpFrom)
        {
            var (nearness, covered) = Scene();
            if (setRight) SubjectEdges.Refine(nearness, covered, Width, Height, Bleed);
            var picture = (float[])rgba.Clone();
            LensBlur.Apply(picture, nearness, Width, Height, sharpFrom, sharpTo: 1f, maxRadius: 6, BokehShape.Circle);
            return picture;
        }

        int row = Height / 2 * Width;
        float Stripe(float[] picture, int x) => Math.Abs(picture[(row + x) * 4] - picture[(row + x + 1) * 4]);

        // A band close round the subject: the rim, whose depth runs out towards the background's, was blurred with it.
        int rim = Edge - 2;
        var tightPlain = Blurred(setRight: false, sharpFrom: 0.85f);
        var tightCrisp = Blurred(setRight: true, sharpFrom: 0.85f);
        Assert.AreEqual(0.2f, Stripe(tightCrisp, rim), 1e-4f, "set right, the rim's stripes are the subject's own, untouched");
        Assert.IsTrue(Stripe(tightPlain, rim) < 0.18f, $"from the depth as it is, the rim was softened: {Stripe(tightPlain, rim):0.000}");

        // A band reaching towards the background: what lies just behind the outline, nearly as near as the subject by the
        // depth as it is, stayed sharp in a halo; set right, it is blurred as the rest of the background is.
        int behind = Edge;
        var wideCrisp = Blurred(setRight: true, sharpFrom: 0.6f);
        var widePlain = Blurred(setRight: false, sharpFrom: 0.6f);
        Assert.IsTrue(Stripe(wideCrisp, behind) < Stripe(widePlain, behind) / 2,
            $"just behind the subject, blurred as the background is: {Stripe(wideCrisp, behind):0.000} against a halo's {Stripe(widePlain, behind):0.000}");
    }
    [TestMethod]
    [CoversNode("images-scene-depth")]
    public void TheSubject_IsPartOfTheLensBlursSection_WrittenAsVersion6_AndReadBackWhole()
    {
        var recipe = ImageRecipe.Identity with
        {
            LensBlur = new RecipeLensBlur { Near = 0.1, Far = 0.3, Subject = new RecipeCutOut { Clicks = [new RecipeCutClick(25, 80, true)] } },
        };
        Assert.AreEqual(6, recipe.WrittenVersion, "the subject goes with the lens blur it belongs to");
        var read = ImageRecipeJson.Deserialize(ImageRecipeJson.Serialize(recipe));
        Assert.AreEqual(recipe, read.Recipe);
        Assert.AreEqual(recipe.LensBlur!.Subject, read.Recipe!.LensBlur!.Subject);
        Assert.AreEqual(recipe.LensBlur.Subject, recipe.LensBlur.Banded(0.4, 0.6).Subject, "the band moved, the subject stays");
    }
}
