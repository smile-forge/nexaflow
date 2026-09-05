using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

using Nexaflow.Visuals.Common.Controls;

namespace Nexaflow.Core.Themes;

/// <summary>
/// The Flowers backdrop: four or five rough horizontal beds with plants growing up out of them —
/// daisies, roses, tulips, poppies, lilies, spikes, bell sprays, pompoms and sunflowers, among
/// leaves, fronds, grasses, gypsophila and berry sprigs — drawn once over unprinted paper and then
/// left completely alone. Theme art for the Flowers theme's <c>StillScene.Window</c>, referenced by
/// nothing else.
///
/// <para><b>A planting uses only some of them.</b> Fourteen motifs are available and a launch draws a
    /// handful — four to six flowers and three or four fillers, chosen from the seed. Showing everything
/// every time averages every launch into the same picture: the variety becomes the constant, and a
/// constant is what the eye stops seeing. A subset gives each launch a character of its own and makes
/// the next one worth looking at.</para>
///
/// <para><b>The composition is beds, not a scatter, and that is the whole of why it reads as planting
/// rather than as wallpaper.</b> Four properties do the work, and losing any one collapses it back
/// into pattern:</para>
/// <list type="number">
///   <item><b>Every plant is rooted on a line.</b> Motifs anchor at the foot of the stem, not at their
///   centre — <c>RenderTransformOrigin</c> is the root — so scaling and leaning both leave that point
///   where it is, the way a real stem behaves.</item>
///   <item><b>Everything grows upward.</b> Orientation is a lean from the root, never a spin. A freely
///   rotated field has no "up", which is exactly the property of a repeating texture, so the eye files
///   it as décor however well each motif is drawn.</item>
///   <item><b>The lean fans away from the centre line.</b> Plants stand near-vertical down the middle
///   and tip progressively outward toward the edges, by <see cref="FanDegrees"/> at the margins. It is
///   a few degrees and it is the difference between a row of plants and a gathered bunch, because it
///   is what a bunch does: held in the middle, opening at the top.</item>
///   <item><b>The greenery runs on its own lines.</b> Beds alternate between flowers and filler, and
///   the filler ones are lifted to sit BETWEEN the flower lines, at a tighter pitch and a little
///   smaller. Mixed into the same rank the greenery reads as a hedge with flowers pushed into it;
///   given its own lines it becomes what the flower rows stand out of.</item>
///   <item><b>Stem length varies independently of head size.</b> Scaling a whole motif gives big
///   flowers on long stems and small ones on short — every plant the same shape at a different size,
///   which is a pattern again. Real planting mixes a tall thin stem next to a short fat one, so the
///   stem is its own parameter and the head keeps its proportions.</item>
/// </list>
///
/// <para>Beds recede upward: the back bed is drawn smaller and paler, which is the whole of the depth
/// available to a still picture, and beds are drawn back-to-front so nearer planting overlaps further.
/// Within a bed the larger plants go down first for the same reason.</para>
///
/// <para><b>Motifs are shaded rather than flat-filled.</b> Petals carry a gradient from a lit tip to a
/// shaded base, discs and berries a radial highlight, leaves a darker half beyond the midrib. Gradients
/// are normally a scene's dominant cost because WPF re-rasterises them every frame — but nothing here
/// animates, so the whole plate is rasterised once into a single cached texture and the shading is free
/// from the second frame onward. Detail is what a still scene can spend without limit and an animated
/// one cannot: a full window of this — some hundreds of plants over nine beds, every one of them
/// gradient-filled — idles at 0.0% of a core, against 0.7% for a shell with no backdrop at all and
/// 10.7% for the animated reef. None of it reaches the frame loop.</para>
///
/// <para>Three things also separate it from every other scene in this folder:</para>
/// <list type="bullet">
///   <item><b>It never animates.</b> It derives from <see cref="AnimatedScene"/> for the build/rebuild
///   cycle and the cache pass, and calls none of the <c>Loop</c>/<c>Animate</c> helpers — so it
///   registers no clocks and the shell's time manager never hears about it. That is what earns it the
///   <c>StillScene.*</c> key, which the battery policy does not suppress: there is no animation to
///   reclaim, and taking it away would cost the theme its art for nothing.</item>
///   <item><b>The whole planting is one child of the layer, so it caches as one texture.</b> The base
///   caches each direct child of <c>SceneLayer</c>; adding two hundred plants individually would buy
///   two hundred textures for a picture that never changes. The per-bed opacity lives on nested
///   canvases, which composite into that same one texture.</item>
///   <item><b>The beds are drawn once per instance, not once per build.</b> Every launch gets a
///   different planting (a new instance, a new seed), but a window <em>resize</em> must not replant it
///   under the user — a rebuild lays out the beds it already has, and simply reaches further along
///   them. Hence the normalised candidate list,
///   generated lazily and then only ever laid out. Two windows open together each get their own
///   planting, which reads as two pressings of the same plate rather than as an inconsistency.</item>
/// </list>
///
/// <para>Colours are literals, which is the scene-art exception to the never-hard-code-a-colour rule:
/// this file <em>is</em> the theme's art, the way <c>OceanReefScene</c> is.</para>
/// </summary>
public partial class FlowerbedScene : AnimatedScene
{
    public FlowerbedScene() => InitializeComponent();

    protected override Panel SceneLayer => Layer;

    /// <summary>Motifs are authored in a nominal 100-unit-wide box and scaled to their planted size.</summary>
    private const double Box = 100;

    /// <summary>Centre of a flower head in box coordinates. Heads occupy roughly y 0..64; the stem runs
    /// from just under the head down to the root, and its length is what varies per plant.</summary>
    private const double HeadY = 32, HeadFoot = 62;

    /// <summary>Slots along a bed, and beds in a planting. Both are ceilings: how many are actually
    /// planted follows from how many fit the region at a FIXED pixel pitch, so a bigger window shows
    /// more of the same planting rather than the same planting stretched. MaxBeds is generous enough to
    /// fill a 4K window at the bed gap below; the ones that fall above the top are never laid out.</summary>
    private const int PerBed = 60, MaxBeds = 24;

    /// <summary>How a filler bed differs from a flower bed: it sits between the flower lines rather
    /// than on them, runs at a tighter pitch, and is drawn a little smaller. Greenery standing in the
    /// same rank as the flowers reads as a hedge with flowers stuck in it; set on its own lines it
    /// reads as what a bunch is actually made of, and the flower rows gain a background.</summary>
    private const double FillerLift = 0.45, FillerPitch = 0.62, FillerScale = 0.82;

    /// <summary>Separation between neighbours along a bed, and between beds, as multiples of a plant's
    /// own size. Multiples of the plant rather than fractions of the window: spacing that is a fraction
    /// of the region changes every time the window is resized, so flowers slide together as it narrows
    /// and drift apart as it widens — which is the one thing a printed plate never does.
    /// <para>The bed gap is deliberately far shorter than a plant is tall, so beds overlap heavily and
    /// the planting reads as one deep border. At about one bed per plant-height the top and the bottom
    /// of the window each looked right on their own and the two did not belong to the same picture —
    /// there was nothing tying the bands together. Continuous from roughly 150px up; ~190 is the
    /// setting, which keeps the depth without the noise 150 brought.</para></summary>
    private const double PitchFactor = 0.72, BedGapFactor = 0.83;

    /// <summary>How far a plant at the very edge leans away from the centre line. Small on purpose: the
    /// pivot is the root, so the head of a tall plant travels a long way for a few degrees.</summary>
    private const double FanDegrees = 15;

    /// <summary>How much of two neighbouring heads may overlap before they are pushed apart. Well
    /// short of 0, because some overlap is what makes a bed look planted rather than laid out — this
    /// only stops the pile-ups that a jittered position produces every few plants by chance.</summary>
    private const double Crowd = 0.62;

    /// <summary>The most a stem's foot can sit to one side of its head, in box units. This is a bend in
    /// the stem, not a lean of the plant — the head stays upright over it either way, which is what
    /// keeps a strongly curved stem from reading as a toppled one.</summary>
    private const double MaxCurve = 30;

    /// <summary>How faint the whole plate sits behind the shell. The motifs are drawn at the plate's
    /// real strength and quietened here in one place, so the balance is one number rather than an alpha
    /// smeared through every brush — and one number is what makes it tunable against the theme's page
    /// veil, which is the other half of the same judgement.</summary>
    private const double PlateOpacity = 0.62;

    // ── The plate's own colours ────────────────────────────────────────────────────────────────
    private static readonly Color[] Blooms =
    [
        Color.FromRgb(0xC4, 0x40, 0x5C),   // zinnia rose
        Color.FromRgb(0xE0, 0x6A, 0x86),   // cosmos pink
        Color.FromRgb(0xC9, 0x27, 0x3C),   // deep red
        Color.FromRgb(0xE4, 0x70, 0x2A),   // orange bloom
        Color.FromRgb(0xE8, 0xA6, 0x2E),   // marigold
        Color.FromRgb(0x7A, 0x64, 0xB0),   // lavender petal
        Color.FromRgb(0xF0, 0x8A, 0x7C),   // coral
        Color.FromRgb(0xCD, 0xAA, 0x1E),   // sunflower ray
    ];

    private static readonly Color[] Greens =
    [
        Color.FromRgb(0x2F, 0x7D, 0x4E),   // stem green
        Color.FromRgb(0x3E, 0x8E, 0x52),   // leaf
        Color.FromRgb(0x57, 0xA0, 0x5A),   // mid green
        Color.FromRgb(0x8C, 0xA8, 0x3F),   // olive frond
        Color.FromRgb(0xA8, 0xC7, 0x9A),   // sage
        Color.FromRgb(0x1F, 0x3D, 0x5C),   // the plate's one navy leaf
    ];

    private static readonly Color[] Berries =
    [
        Color.FromRgb(0x2F, 0xA7, 0x92),   // teal berry
        Color.FromRgb(0xE0, 0x6A, 0x86),   // pink berry
        Color.FromRgb(0xC9, 0x27, 0x3C),   // red berry
        Color.FromRgb(0x2E, 0x9B, 0xC4),   // sky berry
    ];

    /// <summary>Flower hearts and the pale inner rings of a rose — the plate's cream and butter tones.</summary>
    private static readonly Color[] Hearts =
    [
        Color.FromRgb(0xF6, 0xE8, 0xC8),
        Color.FromRgb(0xFA, 0xF2, 0xDE),
        Color.FromRgb(0xE8, 0xA6, 0x2E),
    ];

    private static readonly Color StemColour = Color.FromRgb(0x7C, 0x5F, 0x6D);   // dusty mauve stem

    private enum Motif
    {
        // Flowers
        Daisy, Rose, Tulip, Poppy, Lily, Spike, Bell, Pompom, Sunflower,
        // Filler — the greenery and the small stuff a bunch is mostly made of
        Leaf, Frond, Grass, Gyp, Berries,
    }

    /// <summary>Every flower this plate can draw, and every filler. A planting uses a SUBSET of each
    /// (see <see cref="ChooseSpecies"/>), which is what makes one launch a different bunch from the
    /// last rather than the same mix reshuffled.</summary>
    private static readonly Motif[] AllFlowers =
        [Motif.Daisy, Motif.Rose, Motif.Tulip, Motif.Poppy, Motif.Lily,
         Motif.Spike, Motif.Bell, Motif.Pompom, Motif.Sunflower];

    private static readonly Motif[] AllFiller =
        [Motif.Leaf, Motif.Frond, Motif.Grass, Motif.Gyp, Motif.Berries];

    /// <summary>The flowers that hold up at better than twice size. A rose is concentric rings — a
    /// flower at 70px and a bullseye at 250, because the ring spacing that sells it scales along with
    /// everything else — and a bell spray is fine detail for the same reason.</summary>
    private static readonly Motif[] FeatureFlowers =
        [Motif.Daisy, Motif.Tulip, Motif.Poppy, Motif.Lily, Motif.Sunflower, Motif.Pompom];

    /// <summary>One plant on a bed: where it stands along the bed, and everything about it that must
    /// survive a resize unchanged. <c>Stem</c> is in box units and independent of <c>Scale</c>, which
    /// is what lets a bed hold a tall thin plant beside a short broad one.</summary>
    private readonly record struct Plant(
        int Slot, double Jitter, double Wobble, double Scale, double Stem,
        double CurveMag, bool CurveWithFan, double TiltJitter, Motif Kind, int ColourSeed);

    /// <summary>A bed: its plants, the phase that offsets the whole run along the pitch, and whether it
    /// is a flower bed or a filler one. Without the phase every bed starts its slots at the same place
    /// and the plants line up in columns across the beds — the regularity the jitter was supposed to
    /// hide, showing through it vertically.</summary>
    private readonly record struct Bed(Plant[] Plants, double Phase, bool IsFiller);

    // A different planting every launch: the seed is taken once, when the scene is constructed.
    private readonly int _seed = Random.Shared.Next();
    private Bed[]? _beds;

    protected override void BuildScene(double width, double height)
    {
        var beds = _beds ??= GenerateBeds();

        // Size follows the SHORT edge, and every gap is then a fixed multiple of it in PIXELS. Both
        // were fractions of the region before, so resizing the window changed how far apart the
        // planting stood rather than how much of it you could see. Now the separation is constant and
        // a wider window simply reaches further along each bed; a taller one reveals more beds.
        double basis  = Math.Clamp(Math.Min(width, height) / 7.0, 90, 230);
        double pitch  = basis * PitchFactor;
        double bedGap = basis * BedGapFactor;

        // How many beds the region can actually show. Depth has to be graded over THESE, not over
        // MaxBeds: grading over the ceiling spends most of the fade on beds that are never laid out,
        // and the ones you can see end up nearly the same weight as each other.
        int visible = Math.Clamp((int)((height + bedGap) / bedGap) + 1, 1, beds.Length);

        // Everything into one canvas: the base's cache pass then makes the entire plate one texture.
        var field = new Canvas { Opacity = PlateOpacity };

        // Two layers: every filler bed, then every flower bed. Within each, back to front. Bed 0 is
        // nearest and roots just past the bottom edge, so the front planting is cropped by it — a row of
        // complete plants sitting neatly along the bottom reads as a border print. The rest step up at
        // a fixed gap, and one that lands above the top is simply not planted, which is how a taller
        // window comes to show more beds instead of the same beds spread thinner.
        //
        // Filler goes behind the flowers WHATEVER ladder position it holds. The relaxation below keeps
        // a bed from piling up on itself, but it works within one bed and cannot see the row in front —
        // so a fern crossing a bloom is a case it structurally cannot fix. Ordering the two roles fixes
        // it for nothing, and it is what a bunch does anyway: greenery behind, flowers in front.
        for (int layer = 0; layer < 2; layer++)
        for (int b = beds.Length - 1; b >= 0; b--)
        {
            var bed = beds[b];
            if (bed.IsFiller != (layer == 0)) continue;

            // A filler bed is lifted off the flower ladder so the greenery runs BETWEEN the flower
            // lines rather than in rank with them.
            double bedY = height + bedGap * 0.08 - b * bedGap - (bed.IsFiller ? bedGap * FillerLift : 0);
            if (bedY < -bedGap * 0.35) continue;

            // 1 at the front bed, 0 at the back. Smaller and paler with distance is the entire depth
            // budget of a picture that cannot move, so both cues are spent on it.
            double depth    = visible == 1 ? 1 : 1 - Math.Min(b, visible - 1) / (double)(visible - 1);
            double bedScale = (0.70 + depth * 0.30) * (bed.IsFiller ? FillerScale : 1);
            double rowPitch = pitch * (bed.IsFiller ? FillerPitch : 1);

            // Nested, so the bed's opacity composites into the single cached texture above rather than
            // costing a texture of its own.
            var row     = new Canvas { Opacity = 0.58 + depth * 0.42 };
            var planted = bed.Plants[..Math.Min(PerBed, (int)(width / rowPitch) + 2)];

            // Slots are already in order along the bed, so the prefix IS its left-hand run and a wider
            // window extends it rightward. (It used to be a shuffled sample, which meant widening the
            // window changed *which* plants appeared as well as how many.)
            var px = new double[planted.Length];
            var hw = new double[planted.Length];
            for (int i = 0; i < planted.Length; i++)
            {
                px[i] = (planted[i].Slot + planted[i].Jitter + bed.Phase) * rowPitch;
                // A head is roughly 62 of the 100 box units across; that is what must not collide.
                hw[i] = basis * planted[i].Scale * bedScale * 0.62;
            }

            // Ease the pile-ups. A jittered position is uniform, which means every few plants two land
            // almost on top of each other — and one such pair reads as a mistake more loudly than
            // twenty well-spaced plants read as planting. Two sweeps of pairwise pushing, in x only,
            // and only where heads overlap by more than Crowd: not a packing algorithm, just enough to
            // break up the collisions chance keeps producing.
            for (int sweep = 0; sweep < 2; sweep++)
            {
                for (int i = 1; i < planted.Length; i++)
                    Separate(px, hw, i - 1, i);
                for (int i = planted.Length - 1; i > 0; i--)
                    Separate(px, hw, i - 1, i);
            }

            // Largest first, so the tall plants stand behind the low ones within their own bed.
            var order = Enumerable.Range(0, planted.Length).ToArray();
            Array.Sort(order, (i, j) => planted[j].Scale.CompareTo(planted[i].Scale));

            foreach (int idx in order)
            {
                var    p    = planted[idx];
                double k    = basis * p.Scale * bedScale / Box;
                double root = HeadFoot + p.Stem;          // the foot of this plant's stem, in box units

                // Where this plant ended up across the window, which is what the fan and the bend are
                // both measured from — so both follow the relaxed position rather than the intended one.
                double nx      = width < 1 ? 0.5 : px[idx] / width;
                double fanSign = nx < 0.5 ? -1 : 1;
                double curve   = p.CurveMag * MaxCurve * (p.CurveWithFan ? fanSign : -fanSign);
                double tilt    = (nx - 0.5) * 2 * FanDegrees + p.TiltJitter;

                var motif = Draw(p.Kind, p.Stem, curve, new Random(p.ColourSeed));
                motif.Width  = Box;
                motif.Height = root;

                // The root, not the centre: a plant grows out of the ground and leans from it, so both
                // the scale and the tilt have to leave that one point where it is.
                motif.RenderTransformOrigin = new Point(0.5, 1.0);
                motif.RenderTransform = new TransformGroup
                {
                    Children = { new ScaleTransform(k, k), new RotateTransform(tilt) },
                };

                Canvas.SetLeft(motif, px[idx] - Box / 2);
                Canvas.SetTop (motif, bedY + p.Wobble * bedGap - root);
                row.Children.Add(motif);
            }

            field.Children.Add(row);
        }

        SceneLayer.Children.Add(field);
    }

    /// <summary>Push two neighbours apart, half the shortfall each, if their heads overlap by more
    /// than <see cref="Crowd"/> allows. Both move, so a chain of crowded plants spreads either way
    /// instead of walking the whole bed to the right.</summary>
    private static void Separate(double[] x, double[] w, int a, int b)
    {
        double need = (w[a] + w[b]) * 0.5 * Crowd;
        double gap  = x[b] - x[a];
        if (gap >= need) return;

        double push = (need - gap) * 0.5;
        x[a] -= push;
        x[b] += push;
    }

    /// <summary>
    /// The beds, in slots rather than in coordinates — so they are generated once and merely laid out
    /// at whatever pitch the region turns out to want. Each bed takes its own phase along that pitch,
    /// which is what stops the plants forming columns from one bed to the next.
    /// </summary>
    private Bed[] GenerateBeds()
    {
        var rng     = new Random(_seed);
        var species = ChooseSpecies(rng);
        var beds    = new Bed[MaxBeds];

        // Alternate the roles, starting on either — so the front bed is sometimes flowers and sometimes
        // the greenery they stand out of.
        int roleStart = rng.Next(2);

        for (int b = 0; b < beds.Length; b++)
        {
            bool isFiller = (b + roleStart) % 2 == 1;
            var  bed      = new Plant[PerBed];

            for (int i = 0; i < PerBed; i++)
            {
                // Mostly modest, with roughly one in five a feature bloom: a bed where everything is
                // the same height is a hedge, not planting. Filler has no feature tier — its job is to
                // be the setting, and a giant fern competes with the flowers for the same attention.
                bool feature = !isFiller && rng.NextDouble() < 0.20;
                double scale = feature
                    ? 1.30 + rng.NextDouble() * 0.45
                    : 0.62 + rng.NextDouble() * 0.62;

                // A bed is one thing or the other. Every flower already carries leaves up its own stem,
                // so a pure flower bed is not a bare one — and keeping the two apart is what lets the
                // greenery sit on its own lines at its own pitch.
                var kind = isFiller ? species.Filler[rng.Next(species.Filler.Length)]
                         : feature  ? PickFeatureKind(species.Flowers, rng)
                                    : species.Flowers[rng.Next(species.Flowers.Length)];

                bed[i] = new Plant(
                    Slot:   i,
                    // Under half a pitch either way, so a plant never trades places with its neighbour
                    // and the slots stay in order — which is what lets the prefix be the bed's left run.
                    Jitter: (rng.NextDouble() - 0.5) * 0.84,
                    Wobble: (rng.NextDouble() - 0.5) * 0.10,
                    Scale:  scale,
                    // The independent axis: a wide range, deliberately uncorrelated with Scale, so a bed
                    // mixes tall-and-slight with short-and-broad instead of one silhouette at several
                    // sizes. Filler runs longer and thinner than the flowers it stands behind.
                    Stem: kind is Motif.Frond or Motif.Berries or Motif.Gyp or Motif.Grass
                              ?  75 + rng.NextDouble() * 105
                              :  42 + rng.NextDouble() * 108,
                    // How far the foot of the stem sits from under its head. Squared, so most stems are
                    // near-straight and a few sweep hard — an even spread gives every plant the same
                    // gentle curve, which is a shape rather than a variation. Its direction usually
                    // follows the way the plant leans, because a gathered bunch bends outward together;
                    // sometimes it doesn't, or a whole bed curves as one comb.
                    CurveMag:     Math.Pow(rng.NextDouble(), 2),
                    CurveWithFan: rng.NextDouble() < 0.72,
                    // On top of the fan, which BuildScene applies from where the plant lands. Foliage
                    // swings wider than a flower head, which turns to face you.
                    TiltJitter: (rng.NextDouble() * 2 - 1)
                                * (kind is Motif.Leaf or Motif.Frond or Motif.Grass ? 14 : 7),
                    Kind:       kind,
                    ColourSeed: rng.Next());
            }

            beds[b] = new Bed(bed, rng.NextDouble(), isFiller);
        }

        return beds;
    }

    /// <summary>
    /// This planting's species: a few flowers and a few fillers out of everything the plate can draw.
    /// Drawing all fourteen every time averages every launch into the same picture — the variety
    /// becomes the constant, and a constant is what the eye stops seeing. Three or four flowers and
    /// two or three fillers gives each launch a character, and makes the next one a surprise.
    /// </summary>
    private static (Motif[] Flowers, Motif[] Filler) ChooseSpecies(Random rng) =>
        (Sample(AllFlowers, 4 + rng.Next(3), rng),
         Sample(AllFiller,  3 + rng.Next(2), rng));

    /// <summary><paramref name="n"/> distinct members of <paramref name="bank"/>, chosen uniformly.</summary>
    private static Motif[] Sample(Motif[] bank, int n, Random rng)
    {
        var pool = (Motif[])bank.Clone();
        for (int i = pool.Length - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }
        return pool[..Math.Min(n, pool.Length)];
    }

    /// <summary>A feature plant is one of this planting's own flowers, restricted to those that survive
    /// being enlarged — falling back to a daisy when the bunch happens to contain none of them.</summary>
    private static Motif PickFeatureKind(Motif[] flowers, Random rng)
    {
        var eligible = Array.FindAll(flowers, f => Array.IndexOf(FeatureFlowers, f) >= 0);
        return eligible.Length == 0 ? Motif.Daisy : eligible[rng.Next(eligible.Length)];
    }

    private static Canvas Draw(Motif kind, double stem, double curve, Random rng) => kind switch
    {
        Motif.Daisy     => Daisy(stem, curve, rng),
        Motif.Rose      => Rose(stem, curve, rng),
        Motif.Tulip     => Tulip(stem, curve, rng),
        Motif.Leaf      => Leaf(stem, curve, rng),
        Motif.Berries   => BerrySprig(stem, curve, rng),
        Motif.Gyp       => Gyp(stem, curve, rng),
        Motif.Grass     => Grass(stem, curve, rng),
        Motif.Poppy     => Poppy(stem, curve, rng),
        Motif.Lily      => Lily(stem, curve, rng),
        Motif.Spike     => Spike(stem, curve, rng),
        Motif.Bell      => Bells(stem, curve, rng),
        Motif.Pompom    => Pompom(stem, curve, rng),
        Motif.Sunflower => Sunflower(stem, curve, rng),
        _               => Frond(stem, curve, rng),
    };

    // ── Motifs ────────────────────────────────────────────────────────────────────────────────
    // Each is authored 100 units wide with its head around y=HeadY, its stem running down to
    // y = HeadFoot + stem, and its root at the bottom edge of the element.

    /// <summary>
    /// A stem from just under the head down to the root, carrying a leaf or two. This is what makes the
    /// beds planting rather than a scatter of cut heads: a flower head alone has no sense of growing,
    /// and once every head is on a stem the shared vertical turns a row into a border.
    /// </summary>
    private static void AddStem(Canvas canvas, double top, double stem, double curve, Color green, Random rng)
    {
        double root = HeadFoot + stem;
        var    dark = new SolidColorBrush(Darken(green, 0.16));

        // The bend. A stem that starts and ends at the same x and bows out in the middle is a bulge,
        // not a bend — it was the first thing tried and it read as a swollen stalk. A real stem leaves
        // the ground offset from where its head ends up and straightens as it rises, so the curve is
        // spent entirely between the root and the head rather than out and back. `curve` is that
        // offset, in box units, and at zero the stem is dead straight; the head stays at x=50 either
        // way, so nothing above the stem has to know about it.
        double footX = 50 + curve;
        double ctlX  = 50 + curve * 0.34;
        double ctlY  = (top + root) / 2;

        canvas.Children.Add(new Path
        {
            // Invariant formatting: a geometry string separates coordinates with commas, so a culture
            // that also writes decimals with one turns "72.5,50" into three numbers and the path fails
            // to parse — on that machine only, and silently, since the scene just draws less.
            Data = Geometry.Parse(FormattableString.Invariant(
                $"M {footX},{root} Q {ctlX},{ctlY} 50,{top}")),
            Stroke             = dark,
            StrokeThickness    = 3.2,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap   = PenLineCap.Round,
        });

        // One leaf per ~55 units of stem, so a long stem is clothed and a short one is not crowded.
        // Each sits ON the curve rather than on the straight line the stem no longer is.
        int leaves = Math.Clamp((int)(stem / 55) + 1, 1, 3);
        for (int i = 0; i < leaves; i++)
        {
            double t    = 0.66 - i * 0.26;                    // measured from the root upward
            var (x, y)  = Bend(t, footX, root, ctlX, ctlY, 50, top);
            int    dir  = i % 2 == 0 ? -1 : 1;
            double len  = (0.22 + rng.NextDouble() * 0.16) * (stem + 60);

            var leaf = new Path
            {
                Data = Geometry.Parse(BladePath(len, rng)),
                // Lit along its length: pale where it turns to the light, the base green in its shadow.
                Fill = Sweep(Lighten(green, 0.30), green, horizontal: true),
            };
            leaf.RenderTransform = new TransformGroup
            {
                Children =
                {
                    new ScaleTransform(dir, 1),
                    new RotateTransform(-28 - rng.NextDouble() * 14),
                    new TranslateTransform(x, y),
                },
            };
            canvas.Children.Add(leaf);
        }
    }

    /// <summary>
    /// One blade, pointing along +x from (0,0), and deliberately not the same shape twice. A leaf drawn
    /// from a single symmetric formula is the same leaf at several sizes, and a stem hung with six of
    /// them reads as a machined part rather than as a plant — which is the tell that gives a procedural
    /// scene away fastest. So both edges get their own belly, the widest point slides along the length,
    /// and the tip lifts or droops, none of which costs more than the symmetric version did.
    /// </summary>
    private static string BladePath(double len, Random rng)
    {
        double up1   = len * (0.16 + rng.NextDouble() * 0.17);
        double up2   = len * (0.09 + rng.NextDouble() * 0.21);
        double dn1   = len * (0.07 + rng.NextDouble() * 0.15);
        double dn2   = len * (0.05 + rng.NextDouble() * 0.17);
        double tipY  = len * (rng.NextDouble() * 0.32 - 0.13);   // curled up, or drooping
        double waist = 0.22 + rng.NextDouble() * 0.20;

        // Each fragment invariant on its own: concatenating interpolated strings first produces a plain
        // string, which Invariant cannot then take back and re-format.
        return FormattableString.Invariant(
                   $"M 0,0 C {len * waist},{-up1} {len * (1 - waist)},{-up2} {len},{tipY} ")
             + FormattableString.Invariant(
                   $"C {len * (1 - waist * 0.9)},{tipY + dn2} {len * waist * 0.9},{dn1} 0,0 Z");
    }

    /// <summary>A point on a quadratic bend, so anything hung off a stem can sit on the curve instead
    /// of on the straight line it would have been.</summary>
    private static (double X, double Y) Bend(
        double t, double x0, double y0, double x1, double y1, double x2, double y2)
    {
        double u = 1 - t;
        return (u * u * x0 + 2 * u * t * x1 + t * t * x2,
                u * u * y0 + 2 * u * t * y1 + t * t * y2);
    }

    /// <summary>Radiating strap petals around a disc — the plate's daisies and cosmos. Two rings, the
    /// back one darker and offset by half a step, so the head has a front and a behind.</summary>
    private static Canvas Daisy(double stem, double curve, Random rng)
    {
        var canvas = new Canvas();
        var petal  = Pick(Blooms, rng);
        var heart  = Pick(Hearts, rng);

        AddStem(canvas, HeadY + 10, stem, curve, Pick(Greens, rng), rng);

        int    petals = 8 + rng.Next(6);
        double petalW = 11 + rng.NextDouble() * 4;
        double petalH = 25 + rng.NextDouble() * 8;

        // Back ring first: darker, a little longer, half a step around — the petals you see between the
        // front ones. Without it a daisy is a flat rosette; with it the head has thickness.
        AddPetalRing(canvas, petals, petalW * 0.92, petalH * 1.12, 180.0 / petals,
                     Sweep(Darken(petal, 0.24), Darken(petal, 0.40)));
        AddPetalRing(canvas, petals, petalW, petalH, 0,
                     Sweep(Lighten(petal, 0.26), Darken(petal, 0.10)));

        // Disc: lit from the upper left, with a ring of seed dots inside it.
        double d = 15 + rng.NextDouble() * 7;
        canvas.Children.Add(Dome(50 - d / 2, HeadY - d / 2, d, heart));

        int dots = 6 + rng.Next(4);
        for (int i = 0; i < dots; i++)
        {
            double a = i * 2 * Math.PI / dots + rng.NextDouble() * 0.3;
            var dot = new Ellipse
            {
                Width  = d * 0.16,
                Height = d * 0.16,
                Fill   = new SolidColorBrush(Darken(heart, 0.34)),
            };
            Canvas.SetLeft(dot, 50 + Math.Cos(a) * d * 0.26 - d * 0.08);
            Canvas.SetTop (dot, HeadY + Math.Sin(a) * d * 0.26 - d * 0.08);
            canvas.Children.Add(dot);
        }

        return canvas;
    }

    /// <summary>One ring of strap petals about the head centre.</summary>
    private static void AddPetalRing(Canvas canvas, int petals, double w, double h, double phase, Brush fill)
    {
        for (int i = 0; i < petals; i++)
        {
            var e = new Ellipse { Width = w, Height = h, Fill = fill };
            // Sits directly above the head centre, then rotates about it.
            Canvas.SetLeft(e, 50 - w / 2);
            Canvas.SetTop (e, HeadY - h - 5);
            e.RenderTransform = new RotateTransform(phase + i * 360.0 / petals, w / 2, h + 5);
            canvas.Children.Add(e);
        }
    }

    /// <summary>Concentric offset rings — the reference plate's cabbage rose. Each ring is domed rather
    /// than flat, which is what turns a set of circles into curled petals.</summary>
    private static Canvas Rose(double stem, double curve, Random rng)
    {
        var canvas = new Canvas();
        var outer  = Pick(Blooms, rng);

        AddStem(canvas, HeadY + 8, stem, curve, Pick(Greens, rng), rng);

        int    rings = 4 + rng.Next(2);
        double r     = 29;
        for (int i = 0; i < rings; i++)
        {
            // Each ring drifts off the last, which is what stops it reading as a target, and the drift
            // is toward the light so the shading reads as a curl rather than as an accident.
            double ox = (rng.NextDouble() - 0.5) * 5 - 1.5;
            double oy = (rng.NextDouble() - 0.5) * 5 - 1.5;
            var tone  = i % 2 == 0 ? outer : Lighten(outer, 0.30);

            canvas.Children.Add(new Ellipse
            {
                Width  = r * 2,
                Height = r * 2,
                Fill   = Sweep(Lighten(tone, 0.22), Darken(tone, 0.18)),
                Margin = new Thickness(50 - r + ox, HeadY - r + oy, 0, 0),
            });
            r *= 0.70;
        }

        return canvas;
    }

    /// <summary>Three tapered petals on a stem — the plate's tulips and lotus heads. The outer two are
    /// shaded down so the middle one reads as nearest.</summary>
    private static Canvas Tulip(double stem, double curve, Random rng)
    {
        var canvas = new Canvas();
        var petal  = Pick(Blooms, rng);

        const double Base = 46;                     // where the petals meet the stem
        AddStem(canvas, Base - 4, stem, curve, Pick(Greens, rng), rng);

        foreach (double lean in new[] { -33.0, 33.0 })
        {
            var e = new Ellipse
            {
                Width  = 23,
                Height = 40,
                Fill   = Sweep(Lighten(petal, 0.16), Darken(petal, 0.26)),
            };
            Canvas.SetLeft(e, 50 - 11.5);
            Canvas.SetTop (e, Base - 40);
            e.RenderTransform = new RotateTransform(lean, 11.5, 40);
            canvas.Children.Add(e);
        }

        var centre = new Ellipse
        {
            Width  = 25,
            Height = 46,
            Fill   = Sweep(Lighten(petal, 0.34), Darken(petal, 0.08)),
        };
        Canvas.SetLeft(centre, 50 - 12.5);
        Canvas.SetTop (centre, Base - 46);
        canvas.Children.Add(centre);

        // A highlight down the near petal — the one mark that most says "rounded".
        var gleam = new Ellipse
        {
            Width  = 6,
            Height = 20,
            Fill   = new SolidColorBrush(Lighten(petal, 0.55)) { Opacity = 0.65 },
        };
        Canvas.SetLeft(gleam, 50 - 8);
        Canvas.SetTop (gleam, Base - 40);
        canvas.Children.Add(gleam);

        return canvas;
    }

    /// <summary>A pointed blade on its own stem, with the far half of the blade in shadow beyond the
    /// midrib — the cheapest way to make a flat shape look folded.</summary>
    private static Canvas Leaf(double stem, double curve, Random rng)
    {
        var canvas   = new Canvas();
        var green    = Pick(Greens, rng);
        double belly = 17 + rng.NextDouble() * 9;
        double tip   = HeadY - 30;
        double foot  = HeadFoot + stem;

        canvas.Children.Add(new Path
        {
            Data = Geometry.Parse(FormattableString.Invariant($"M {50 + curve},{foot} Q {50 + curve * 0.34},{(foot + HeadFoot) / 2} 50,{HeadFoot - 4}")),
            Stroke             = new SolidColorBrush(Darken(green, 0.16)),
            StrokeThickness    = 3,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap   = PenLineCap.Round,
        });

        // Lit half, then the shaded half laid over it, then the midrib along the seam.
        // The two sides get their own belly and their own shoulder, so the blade is not a mirror of
        // itself — and the shaded half is then genuinely a different shape from the lit one.
        double belly2 = belly * (0.62 + rng.NextDouble() * 0.5);
        double sh1    = HeadY + (rng.NextDouble() - 0.5) * 12;
        double sh2    = HeadY + (rng.NextDouble() - 0.5) * 12;

        canvas.Children.Add(new Path
        {
            Data = Geometry.Parse(FormattableString.Invariant(
                $"M 50,{tip} Q {50 + belly},{sh1} 50,{HeadFoot} Q {50 - belly2},{sh2} 50,{tip} Z")),
            Fill = Sweep(Lighten(green, 0.26), green),
        });
        canvas.Children.Add(new Path
        {
            Data = Geometry.Parse(FormattableString.Invariant(
                $"M 50,{tip} Q {50 + belly},{sh1} 50,{HeadFoot} L 50,{tip} Z")),
            Fill = new SolidColorBrush(Darken(green, 0.20)) { Opacity = 0.7 },
        });
        canvas.Children.Add(new Path
        {
            Data = Geometry.Parse(FormattableString.Invariant(
                $"M 50,{tip + 5} L 50,{HeadFoot - 4}")),
            Stroke          = new SolidColorBrush(Lighten(green, 0.44)),
            StrokeThickness = 2.2,
        });

        return canvas;
    }

    /// <summary>A curved stem hung with berries — the plate's teal and pink berry sprigs. Each berry is
    /// domed, which at this size is most of what makes a circle read as fruit.</summary>
    private static Canvas BerrySprig(double stem, double curve, Random rng)
    {
        var    canvas = new Canvas();
        var    berry  = Pick(Berries, rng);
        double foot   = HeadFoot + stem;
        double top    = HeadY - 28;
        double bend   = (rng.NextDouble() - 0.5) * 34;

        canvas.Children.Add(new Path
        {
            Data = Geometry.Parse(FormattableString.Invariant(
                $"M {50 + curve},{foot} Q {50 + curve * 0.34 + bend},{(top + foot) / 2} 50,{top}")),
            Stroke             = new SolidColorBrush(StemColour),
            StrokeThickness    = 2.6,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap   = PenLineCap.Round,
        });

        int berries = 6 + rng.Next(5);
        for (int i = 0; i < berries; i++)
        {
            double t = (i + 0.5) / berries;                  // 0 at the root, 1 at the tip
            double d = 9 + rng.NextDouble() * 7;
            double x = 50 + bend * 4 * t * (1 - t)           // follow the stem's curve
                          + (i % 2 == 0 ? -1 : 1) * (7 + rng.NextDouble() * 5);
            double y = foot - t * (foot - top);
            canvas.Children.Add(Dome(x - d / 2, y - d / 2, d, berry));
        }

        return canvas;
    }

    /// <summary>A stem with paired leaflets — the plate's ferns and grasses.</summary>
    private static Canvas Frond(double stem, double curve, Random rng)
    {
        var    canvas = new Canvas();
        var    green  = Pick(Greens, rng);
        double foot   = HeadFoot + stem;
        double top    = HeadY - 30;
        var    fill   = Sweep(Lighten(green, 0.24), Darken(green, 0.12), horizontal: true);

        canvas.Children.Add(new Path
        {
            Data = Geometry.Parse(FormattableString.Invariant(
                $"M {50 + curve},{foot} Q {54 + curve * 0.34},{(top + foot) / 2} 50,{top}")),
            Stroke             = new SolidColorBrush(Darken(green, 0.14)),
            StrokeThickness    = 2.6,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap   = PenLineCap.Round,
        });

        int pairs = Math.Clamp((int)(stem / 14) + 3, 4, 11);
        for (int i = 0; i < pairs; i++)
        {
            double t = (i + 0.6) / (pairs + 0.4);
            double y = foot - t * (foot - top);
            // Leaflets shorten toward the tip, which is what makes it read as a frond and not a comb.
            double len = (16 - t * 9) * (0.8 + rng.NextDouble() * 0.4);

            foreach (int dir in new[] { -1, 1 })
            {
                var e = new Ellipse { Width = len, Height = 7.5, Fill = fill };
                Canvas.SetLeft(e, dir < 0 ? 50 - len : 50);
                Canvas.SetTop (e, y - 3.75);
                e.RenderTransform = new RotateTransform(dir * -32, dir < 0 ? len : 0, 3.75);
                canvas.Children.Add(e);
            }
        }

        return canvas;
    }

    /// <summary>
    /// Gypsophila: a stem that forks into sprays, each ending in a knot of tiny pale florets. The
    /// filler a florist packs a bunch out with — it reads as nothing in particular up close and as
    /// air between the flowers at this size, which is exactly its job here too.
    /// </summary>
    private static Canvas Gyp(double stem, double curve, Random rng)
    {
        var    canvas = new Canvas();
        var    green  = Pick(Greens, rng);
        var    floret = Pick(Hearts, rng);
        var    twig   = new SolidColorBrush(Lighten(green, 0.10));
        double foot   = HeadFoot + stem;
        double top    = HeadY - 30;

        canvas.Children.Add(new Path
        {
            Data = Geometry.Parse(FormattableString.Invariant(
                $"M {50 + curve},{foot} Q {53 + curve * 0.34},{(top + foot) / 2} 50,{top + 14}")),
            Stroke             = twig,
            StrokeThickness    = 1.9,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap   = PenLineCap.Round,
        });

        int sprays = 4 + rng.Next(4);
        for (int i = 0; i < sprays; i++)
        {
            // Sprays leave the stem in its upper half and reach up and out; the lowest are the longest,
            // which is what gives a gyp stem its cone.
            double t    = 0.34 + (i + rng.NextDouble()) / (sprays + 1) * 0.66;
            double y    = foot - t * (foot - top);
            int    dir  = i % 2 == 0 ? -1 : 1;
            double len  = (13 + rng.NextDouble() * 12) * (1.25 - t * 0.5);
            double tipX = 50 + dir * len;
            double tipY = y - len * (0.55 + rng.NextDouble() * 0.5);

            canvas.Children.Add(new Path
            {
                Data = Geometry.Parse(FormattableString.Invariant(
                    $"M 50,{y} Q {50 + dir * len * 0.4},{y - len * 0.15} {tipX},{tipY}")),
                Stroke          = twig,
                StrokeThickness = 1.2,
            });

            int florets = 3 + rng.Next(4);
            for (int f = 0; f < florets; f++)
            {
                double d = 2.6 + rng.NextDouble() * 2.4;
                var e = new Ellipse { Width = d, Height = d, Fill = new SolidColorBrush(floret) };
                Canvas.SetLeft(e, tipX + (rng.NextDouble() - 0.5) * 9 - d / 2);
                Canvas.SetTop (e, tipY + (rng.NextDouble() - 0.5) * 9 - d / 2);
                canvas.Children.Add(e);
            }
        }

        return canvas;
    }

    /// <summary>
    /// Grasses: a fan of tapered blades from one root. Drawn as filled slivers rather than strokes,
    /// because a blade of grass is wide at the base and a point at the tip, and a stroke of even width
    /// reads as wire. The outer blades arc hardest, so the fan opens.
    /// </summary>
    private static Canvas Grass(double stem, double curve, Random rng)
    {
        var    canvas = new Canvas();
        var    green  = Pick(Greens, rng);
        double foot   = HeadFoot + stem;

        int blades = 5 + rng.Next(5);
        for (int i = 0; i < blades; i++)
        {
            // Spread across the fan, with the sign alternating so it fills out from the middle rather
            // than growing all one way.
            double across = (i / (double)Math.Max(1, blades - 1) - 0.5) * 2;   // -1 .. 1
            double reach  = across * (16 + rng.NextDouble() * 26);
            double rise   = (foot - HeadY + 26) * (0.55 + rng.NextDouble() * 0.5)
                            * (1 - Math.Abs(across) * 0.22);
            double tipX   = 50 + reach;
            double tipY   = foot - rise;
            double baseW  = 2.6 + rng.NextDouble() * 2.2;

            canvas.Children.Add(new Path
            {
                Data = Geometry.Parse(
                    FormattableString.Invariant($"M {50 + curve - baseW / 2},{foot} ")
                    + FormattableString.Invariant(
                        $"Q {50 + curve * 0.5 + reach * 0.22},{foot - rise * 0.55} {tipX},{tipY} ")
                    + FormattableString.Invariant(
                        $"Q {50 + curve * 0.5 + reach * 0.30},{foot - rise * 0.52} {50 + curve + baseW / 2},{foot} Z")),
                // Pale at the tip where it catches the light, deeper at the root in the clump's shade.
                Fill = Sweep(Darken(green, 0.10), Lighten(green, 0.34)),
            });
        }

        return canvas;
    }

    /// <summary>A petal pointing straight up from (0,0): <paramref name="len"/> long, <paramref name="w"/>
    /// across at its widest, and coming to a point. The shape most of the new flowers are built from —
    /// rotate copies of it about a centre and the silhouette is whatever the angles say it is.</summary>
    private static string PetalPath(double len, double w) => FormattableString.Invariant(
        $"M 0,0 Q {w / 2},{-len * 0.42} 0,{-len} Q {-w / 2},{-len * 0.42} 0,0 Z");

    /// <summary>Places <paramref name="shape"/> at the head centre, turned by <paramref name="angle"/>.</summary>
    private static void AtHead(Canvas canvas, Shape shape, double angle)
    {
        shape.RenderTransform = new TransformGroup
        {
            Children = { new RotateTransform(angle), new TranslateTransform(50, HeadY) },
        };
        canvas.Children.Add(shape);
    }

    /// <summary>Four or five broad round petals about a dark eye, with a ring of stamens — the poppy,
    /// and the same build serves for a buttercup or a hibiscus depending on the colour it draws.</summary>
    private static Canvas Poppy(double stem, double curve, Random rng)
    {
        var canvas = new Canvas();
        var petal  = Pick(Blooms, rng);
        AddStem(canvas, HeadY + 12, stem, curve, Pick(Greens, rng), rng);

        int    petals = 4 + rng.Next(2);
        double len    = 30 + rng.NextDouble() * 8;
        var    fill   = Sweep(Lighten(petal, 0.30), Darken(petal, 0.16));

        for (int i = 0; i < petals; i++)
            AtHead(canvas, new Path { Data = Geometry.Parse(PetalPath(len, len * 1.35)), Fill = fill },
                   i * 360.0 / petals + rng.NextDouble() * 6);

        double d = 9 + rng.NextDouble() * 5;
        canvas.Children.Add(Dome(50 - d / 2, HeadY - d / 2, d, Darken(petal, 0.72)));

        int stamens = 7 + rng.Next(5);
        for (int i = 0; i < stamens; i++)
        {
            double a  = i * 2 * Math.PI / stamens;
            double rr = d * 0.75;
            var dot = new Ellipse { Width = 2.6, Height = 2.6, Fill = new SolidColorBrush(Darken(petal, 0.6)) };
            Canvas.SetLeft(dot, 50 + Math.Cos(a) * rr - 1.3);
            Canvas.SetTop (dot, HeadY + Math.Sin(a) * rr - 1.3);
            canvas.Children.Add(dot);
        }

        return canvas;
    }

    /// <summary>Six pointed petals in a star with stamens reaching out of the throat — the lily, and the
    /// one flower here whose silhouette is spiky rather than round.</summary>
    private static Canvas Lily(double stem, double curve, Random rng)
    {
        var canvas = new Canvas();
        var petal  = Pick(Blooms, rng);
        AddStem(canvas, HeadY + 10, stem, curve, Pick(Greens, rng), rng);

        double len = 32 + rng.NextDouble() * 8;

        // Back three, then front three offset by half a turn: the same two-ring trick as the daisy.
        for (int ring = 0; ring < 2; ring++)
        {
            var fill = ring == 0
                ? Sweep(Darken(petal, 0.20), Darken(petal, 0.38))
                : Sweep(Lighten(petal, 0.34), Darken(petal, 0.06));
            for (int i = 0; i < 3; i++)
                AtHead(canvas,
                       new Path
                       {
                           Data = Geometry.Parse(PetalPath(len * (ring == 0 ? 1.04 : 1), len * 0.46)),
                           Fill = fill,
                       },
                       ring * 60 + i * 120);
        }

        // Stamens: a short line out of the throat with a heavy anther on the end.
        var anther = new SolidColorBrush(Darken(Pick(Hearts, rng), 0.34));
        const int Count = 5;
        for (int i = 0; i < Count; i++)
        {
            double a = -90 + (i - (Count - 1) / 2.0) * 15;
            double r = len * 0.42;
            double x = 50 + Math.Cos(a * Math.PI / 180) * r;
            double y = HeadY + Math.Sin(a * Math.PI / 180) * r;

            canvas.Children.Add(new Path
            {
                Data = Geometry.Parse(FormattableString.Invariant($"M 50,{HeadY} L {x},{y}")),
                Stroke          = anther,
                StrokeThickness = 1.2,
            });

            var head = new Ellipse { Width = 3.4, Height = 2.2, Fill = anther };
            Canvas.SetLeft(head, x - 1.7);
            Canvas.SetTop (head, y - 1.1);
            canvas.Children.Add(head);
        }

        return canvas;
    }

    /// <summary>A tapering column of small florets — lavender, hyacinth, gladiolus. Tall and narrow, so
    /// it does for a bunch what a vertical does for a composition: breaks a skyline of round heads.</summary>
    private static Canvas Spike(double stem, double curve, Random rng)
    {
        var canvas = new Canvas();
        var petal  = Pick(Blooms, rng);
        AddStem(canvas, HeadY + 20, stem, curve, Pick(Greens, rng), rng);

        double bottom = HeadY + 24;
        double top    = HeadY - 34;
        int    rows   = 10 + rng.Next(8);

        for (int i = 0; i < rows; i++)
        {
            double t = i / (double)(rows - 1);                 // 0 at the base, 1 at the tip
            double y = bottom - t * (bottom - top);
            double w = (7.5 - t * 5.5) * (0.85 + rng.NextDouble() * 0.3);

            // Florets pair either side of the stem, tighter and smaller toward the tip.
            foreach (int dir in new[] { -1, 1 })
            {
                var e = new Ellipse
                {
                    Width  = w,
                    Height = w * 1.25,
                    Fill   = Sweep(Lighten(petal, 0.34 - t * 0.10), Darken(petal, 0.10 + t * 0.12)),
                };
                Canvas.SetLeft(e, 50 + dir * (w * 0.42) - w / 2);
                Canvas.SetTop (e, y - w * 0.62);
                canvas.Children.Add(e);
            }
        }

        return canvas;
    }

    /// <summary>Bells hanging off an arching stem — bluebell, fuchsia. The only motif here that hangs
    /// rather than faces, which is what makes it read as a different plant at a glance.</summary>
    private static Canvas Bells(double stem, double curve, Random rng)
    {
        var    canvas = new Canvas();
        var    petal  = Pick(Blooms, rng);
        var    green  = Pick(Greens, rng);
        double foot   = HeadFoot + stem;
        double top    = HeadY - 26;
        int    side   = rng.Next(2) == 0 ? -1 : 1;
        double arc    = side * (10 + rng.NextDouble() * 10);
        var    twig   = new SolidColorBrush(Darken(green, 0.14));

        canvas.Children.Add(new Path
        {
            Data = Geometry.Parse(FormattableString.Invariant(
                $"M {50 + curve},{foot} Q {50 + curve * 0.34 + arc * 0.4},{(top + foot) / 2} {50 + arc},{top}")),
            Stroke             = twig,
            StrokeThickness    = 2.4,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap   = PenLineCap.Round,
        });

        int bells = 3 + rng.Next(3);
        for (int i = 0; i < bells; i++)
        {
            double t = 0.34 + (i + 0.5) / bells * 0.62;
            double y = foot - t * (foot - top);
            double x = 50 + arc * t * t + side * 3;
            double w = 9 + rng.NextDouble() * 4;
            double h = w * (1.15 + rng.NextDouble() * 0.4);

            // A short pedicel, then the bell: rounded shoulders flaring to an open mouth.
            canvas.Children.Add(new Path
            {
                Data = Geometry.Parse(FormattableString.Invariant(
                    $"M {x - side * 3},{y} L {x},{y + 4}")),
                Stroke          = twig,
                StrokeThickness = 1.2,
            });
            canvas.Children.Add(new Path
            {
                // Each fragment invariant on its own: concatenating interpolated strings first produces
                // a plain string, which Invariant cannot then take back and re-format.
                Data = Geometry.Parse(
                    FormattableString.Invariant($"M {x - w / 2},{y + 4} ")
                    + FormattableString.Invariant(
                        $"C {x - w / 2},{y + 4 + h * 0.8} {x - w * 0.62},{y + 4 + h} {x},{y + 4 + h} ")
                    + FormattableString.Invariant(
                        $"C {x + w * 0.62},{y + 4 + h} {x + w / 2},{y + 4 + h * 0.8} {x + w / 2},{y + 4} Z")),
                Fill = Sweep(Lighten(petal, 0.32), Darken(petal, 0.22)),
            });
        }

        return canvas;
    }

    /// <summary>A dense ball of small petals in rings — dahlia, globe amaranth, carnation. The heaviest
    /// head on the plate, so it anchors a bed the way a big round flower anchors a bunch.</summary>
    private static Canvas Pompom(double stem, double curve, Random rng)
    {
        var canvas = new Canvas();
        var petal  = Pick(Blooms, rng);
        AddStem(canvas, HeadY + 14, stem, curve, Pick(Greens, rng), rng);

        double r     = 27 + rng.NextDouble() * 6;
        int    rings = 3 + rng.Next(2);

        for (int ring = 0; ring < rings; ring++)
        {
            double rr    = r * (1 - ring * 0.27);
            int    count = Math.Max(5, (int)(rr * 0.62));
            // Outer rings darker: a ball is lit on the top of its dome and shaded at the rim.
            var fill = Sweep(Lighten(petal, 0.16 + ring * 0.14),
                             Darken(petal, Math.Max(0.02, 0.26 - ring * 0.14)));

            for (int i = 0; i < count; i++)
            {
                double a   = i * 360.0 / count + ring * 11;
                double rad = a * Math.PI / 180;
                var e = new Ellipse { Width = rr * 0.52, Height = rr * 0.66, Fill = fill };
                Canvas.SetLeft(e, 50 + Math.Cos(rad) * rr * 0.66 - rr * 0.26);
                Canvas.SetTop (e, HeadY + Math.Sin(rad) * rr * 0.66 - rr * 0.33);
                e.RenderTransform = new RotateTransform(a + 90, rr * 0.26, rr * 0.33);
                canvas.Children.Add(e);
            }
        }

        double d = r * 0.34;
        canvas.Children.Add(Dome(50 - d / 2, HeadY - d / 2, d, Lighten(petal, 0.40)));
        return canvas;
    }

    /// <summary>A big seeded disc ringed with narrow rays. The plate's one flower that is mostly centre,
    /// which is what tells it apart from a daisy at any size.</summary>
    private static Canvas Sunflower(double stem, double curve, Random rng)
    {
        var canvas = new Canvas();
        var ray    = rng.Next(2) == 0
            ? Color.FromRgb(0xE8, 0xA6, 0x2E)
            : Color.FromRgb(0xCD, 0xAA, 0x1E);
        AddStem(canvas, HeadY + 16, stem, curve, Pick(Greens, rng), rng);

        int    rays = 16 + rng.Next(8);
        double len  = 30 + rng.NextDouble() * 7;

        // Two turns of rays, the back one darker and offset, so the collar has depth.
        for (int ring = 0; ring < 2; ring++)
        {
            var fill = ring == 0
                ? Sweep(Darken(ray, 0.22), Darken(ray, 0.40))
                : Sweep(Lighten(ray, 0.30), Darken(ray, 0.06));
            for (int i = 0; i < rays; i++)
                AtHead(canvas,
                       new Path
                       {
                           Data = Geometry.Parse(PetalPath(len * (ring == 0 ? 1.1 : 1), len * 0.30)),
                           Fill = fill,
                       },
                       ring * (180.0 / rays) + i * 360.0 / rays);
        }

        var    seedColour = Color.FromRgb(0x5A, 0x3A, 0x1E);
        double d          = 24 + rng.NextDouble() * 6;
        canvas.Children.Add(Dome(50 - d / 2, HeadY - d / 2, d, seedColour));

        // A couple of seed whorls: enough texture to read as a head rather than as a disc.
        var seed = new SolidColorBrush(Lighten(seedColour, 0.28));
        for (int whorl = 1; whorl <= 2; whorl++)
        {
            double rr = d * 0.16 * whorl;
            int    n  = 6 * whorl;
            for (int i = 0; i < n; i++)
            {
                double a = i * 2 * Math.PI / n + whorl * 0.4;
                var dot = new Ellipse { Width = 2, Height = 2, Fill = seed };
                Canvas.SetLeft(dot, 50 + Math.Cos(a) * rr - 1);
                Canvas.SetTop (dot, HeadY + Math.Sin(a) * rr - 1);
                canvas.Children.Add(dot);
            }
        }

        return canvas;
    }

    // ── Shading helpers ───────────────────────────────────────────────────────────────────────
    // All three build gradients. That is normally the expensive thing to put in a scene, because WPF
    // re-rasterises a gradient fill every frame it is drawn — but this scene draws exactly once and is
    // then a cached texture, so the cost is paid at build and never again.

    /// <summary>A lit-to-shaded sweep along a shape's own axis: down its length by default, across it
    /// when <paramref name="horizontal"/>. Petals and leaflets are drawn tip-first, so "down" is from
    /// the lit tip toward the shaded base.</summary>
    private static LinearGradientBrush Sweep(Color lit, Color shade, bool horizontal = false) =>
        new(lit, shade,
            new Point(horizontal ? 0 : 0.35, horizontal ? 0.3 : 0),
            new Point(horizontal ? 1 : 0.65, horizontal ? 0.7 : 1));

    /// <summary>A sphere lit from the upper left — a disc, a berry, a seed head.</summary>
    private static Ellipse Dome(double left, double top, double d, Color c)
    {
        var fill = new RadialGradientBrush
        {
            GradientOrigin = new Point(0.36, 0.32),
            Center         = new Point(0.5, 0.5),
            RadiusX        = 0.62,
            RadiusY        = 0.62,
            GradientStops  =
            {
                new GradientStop(Lighten(c, 0.42), 0),
                new GradientStop(c, 0.62),
                new GradientStop(Darken(c, 0.22), 1),
            },
        };
        var e = new Ellipse { Width = d, Height = d, Fill = fill };
        Canvas.SetLeft(e, left);
        Canvas.SetTop (e, top);
        return e;
    }

    private static Color Pick(Color[] bank, Random rng) => bank[rng.Next(bank.Length)];

    /// <summary>Mix toward white. Both of these keep a motif's tones on one hue, so the shading reads as
    /// light falling on a thing rather than as two colours.</summary>
    private static Color Lighten(Color c, double amount) => Color.FromRgb(
        (byte)(c.R + (255 - c.R) * amount),
        (byte)(c.G + (255 - c.G) * amount),
        (byte)(c.B + (255 - c.B) * amount));

    /// <summary>Mix toward black.</summary>
    private static Color Darken(Color c, double amount) => Color.FromRgb(
        (byte)(c.R * (1 - amount)),
        (byte)(c.G * (1 - amount)),
        (byte)(c.B * (1 - amount)));
}
