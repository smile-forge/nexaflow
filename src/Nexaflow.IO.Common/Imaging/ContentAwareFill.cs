namespace Nexaflow.IO.Common.Imaging;

/// <summary>
/// Fills what a mask covers with the picture's own texture, as content-aware fill does — no neural network: every hole
/// pixel made from the patches elsewhere in the picture that best match the patches round it (Wexler, Shechtman and Irani's
/// completion, its nearest patches found by Barnes et al.'s PatchMatch), worked from a small copy of the picture up to its
/// own size, so a hole takes the large shapes of its surroundings first and their fine texture last.
/// <para>
/// Patches are compared on their colour and on how much texture they hold — each pixel's local gradient, filled with the
/// colour (Newson et al.) — so a hole is not filled with whatever smooth patch matches the blur it starts as. The
/// coarsest level starts from Criminisi et al.'s exemplar fill, which fills first where the picture's lines run into the
/// hole, so an edge is carried across it; and each level's fill is remade from the colours and the steps between pixels
/// its patches copy, by a screened Poisson solve (Darabi et al.'s Image Melding), which keeps copied texture and leaves
/// no seam where one patch meets the next.
/// </para>
/// <para>
/// Each part of the mask is worked in a region of its surroundings (<see cref="InpaintRegions"/>, at the region's own
/// size), and its patches come from that region. Copied texture stays sharp — grass, walls, road, sky, fabric — where a
/// learned fill smooths; a straight edge or a face running through a hole is a learned fill's strength instead. The same
/// picture, mask and seed fill the same way every time.
/// </para>
/// </summary>
public static class ContentAwareFill
{
    /// <summary>The side of the square patches compared.</summary>
    public const int PatchSide = 7;

    private const int Radius = PatchSide / 2;

    /// <summary>What a pixel carries: red, green and blue from 0 to 255, then its texture across and down.</summary>
    private const int Channels = 5;

    /// <summary>How much a difference in texture counts against one in colour.</summary>
    private const float TextureWeight = 2f;

    /// <summary>A level is not made smaller once its hole fits in this many patches each way, or the level itself would
    /// hold too few patches to copy from.</summary>
    private const int CoarsestHolePatches = 2;

    private const int PatchMatchRounds = 4;

    /// <summary>
    /// <paramref name="rgba"/> — interleaved 8-bit RGBA, row by row — with what <paramref name="mask"/> covers (one byte a
    /// pixel, filled where it is not 0) filled from the rest of the picture round it; every other pixel, and the alpha of
    /// all, as it was.
    /// </summary>
    public static byte[] Fill(ReadOnlySpan<byte> rgba, int width, int height, ReadOnlySpan<byte> mask, int seed = 0,
                              IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width), "A picture has a size.");
        if (rgba.Length != width * height * 4) throw new ArgumentException("The pixels are not the size given.", nameof(rgba));
        if (mask.Length != width * height) throw new ArgumentException("The mask is not the picture's size.", nameof(mask));

        var result = rgba.ToArray();
        var regions = InpaintRegions.Of(mask, width, height);
        var holes = mask.ToArray();
        for (int i = 0; i < regions.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            int done = i;
            var step = progress is null ? null : new Within(f => progress.Report((done + f) / regions.Count));
            new Region(result, holes, width, regions[i], seed + i, step, ct).Fill();
        }
        progress?.Report(1);
        return result;
    }

    /// <summary>A region's progress, 0 to 1, reported as its share of the whole fill's.</summary>
    private sealed class Within(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }

    /// <summary>One level of a region's pyramid: what each pixel carries (<see cref="Channels"/> floats) and its hole.</summary>
    private sealed class Level(int width, int height)
    {
        public readonly int Width = width, Height = height;
        public readonly float[] Values = new float[width * height * Channels];
        public readonly bool[] Hole = new bool[width * height];

        /// <summary>Half the size: each pixel the mean of the known pixels of its 2×2, a hole where any of the four is.</summary>
        public Level Half()
        {
            var half = new Level(Math.Max(1, Width / 2), Math.Max(1, Height / 2));
            Span<float> sum = stackalloc float[Channels];
            for (int y = 0; y < half.Height; y++)
                for (int x = 0; x < half.Width; x++)
                {
                    sum.Clear();
                    int known = 0;
                    bool hole = false;
                    for (int dy = 0; dy < 2; dy++)
                        for (int dx = 0; dx < 2; dx++)
                        {
                            int s = Math.Min(Height - 1, 2 * y + dy) * Width + Math.Min(Width - 1, 2 * x + dx);
                            if (Hole[s]) { hole = true; continue; }
                            for (int c = 0; c < Channels; c++) sum[c] += Values[s * Channels + c];
                            known++;
                        }
                    int p = y * half.Width + x;
                    half.Hole[p] = hole;
                    if (known > 0) for (int c = 0; c < Channels; c++) half.Values[p * Channels + c] = sum[c] / known;
                }
            return half;
        }

        /// <summary>The hole's widest extent, in pixels.</summary>
        public int HoleExtent()
        {
            int left = Width, top = Height, right = -1, bottom = -1;
            for (int p = 0; p < Hole.Length; p++)
                if (Hole[p])
                {
                    int x = p % Width, y = p / Width;
                    (left, top, right, bottom) = (Math.Min(left, x), Math.Min(top, y), Math.Max(right, x), Math.Max(bottom, y));
                }
            return right < 0 ? 0 : Math.Max(right - left + 1, bottom - top + 1);
        }

        /// <summary>The mean squared difference of the patches centred on <paramref name="target"/> and
        /// <paramref name="source"/>, over the target patch's pixels inside the level that <paramref name="counted"/> allows
        /// (all, where it is null); stops once it passes <paramref name="bound"/>.</summary>
        public float Distance(int target, int source, float bound, bool[]? counted = null)
        {
            int tx = target % Width, ty = target / Width, sx = source % Width, sy = source / Width;
            float sum = 0;
            int count = 0;
            float limit = bound == float.MaxValue ? float.MaxValue : bound * PatchSide * PatchSide;
            for (int dy = -Radius; dy <= Radius; dy++)
            {
                int y = ty + dy;
                if ((uint)y >= (uint)Height) continue;
                for (int dx = -Radius; dx <= Radius; dx++)
                {
                    int x = tx + dx;
                    if ((uint)x >= (uint)Width) continue;
                    int at = y * Width + x;
                    if (counted is not null && !counted[at]) continue;
                    int a = at * Channels, b = ((sy + dy) * Width + sx + dx) * Channels;
                    for (int c = 0; c < Channels; c++)
                    {
                        float d = Values[a + c] - Values[b + c];
                        sum += d * d;
                    }
                    count++;
                }
                if (sum > limit) return float.MaxValue;
            }
            return count == 0 ? float.MaxValue : sum / count;
        }
    }

    /// <summary>A region filled level by level, coarsest first.</summary>
    private sealed class Region(byte[] rgba, byte[] mask, int pictureWidth, InpaintRegion box, int seed, IProgress<double>? progress, CancellationToken ct)
    {
        public void Fill()
        {
            var levels = new List<Level> { Read() };
            while (levels[^1].HoleExtent() > CoarsestHolePatches * PatchSide
                   && Math.Min(levels[^1].Width, levels[^1].Height) / 2 >= 4 * PatchSide)
                levels.Add(levels[^1].Half());

            Seed(levels[^1]);
            Field? field = null;
            for (int l = levels.Count - 1; l >= 0; l--)
            {
                ct.ThrowIfCancellationRequested();
                var level = levels[l];
                var sources = new Sources(level);
                if (sources.Count > 0)
                {
                    field = field is null ? Field.Random(level, sources, seed) : field.Doubled(level, sources, seed);
                    if (l < levels.Count - 1) field.Vote(level);
                    int rounds = l == levels.Count - 1 ? 10 : l == 0 ? 4 : 6;
                    for (int round = 0; round < rounds; round++)
                    {
                        ct.ThrowIfCancellationRequested();
                        field.Match(level, sources, seed + 7919 * (l * 64 + round));
                        field.Vote(level);
                    }
                }
                else
                {
                    // Too little picture round the hole at this size to copy whole patches from: the level keeps what the
                    // coarser one (or the seed) gave it.
                    field = null;
                }
                // The finer level starts from this one's fill, which its own field then improves on where it has
                // patches to copy from.
                if (l > 0) Spread(level, levels[l - 1]);
                progress?.Report((double)(levels.Count - l) / levels.Count);
            }
            Write(levels[0]);
        }

        /// <summary>The region at its own size: colour, and the texture round each known pixel — the mean, over a patch,
        /// of the brightness's change across and down, counting only known pixels.</summary>
        private Level Read()
        {
            int w = box.Width, h = box.Height;
            var level = new Level(w, h);
            var brightness = new float[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int from = (box.Top + y) * pictureWidth + box.Left + x, to = y * w + x;
                    level.Hole[to] = mask[from] != 0;
                    for (int c = 0; c < 3; c++) level.Values[to * Channels + c] = rgba[from * 4 + c];
                    brightness[to] = 0.299f * rgba[from * 4] + 0.587f * rgba[from * 4 + 1] + 0.114f * rgba[from * 4 + 2];
                }
            var across = new float[w * h];
            var down = new float[w * h];
            var has = new bool[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int p = y * w + x;
                    if (level.Hole[p]) continue;
                    int l = x > 0 && !level.Hole[p - 1] ? p - 1 : p, r = x < w - 1 && !level.Hole[p + 1] ? p + 1 : p;
                    int u = y > 0 && !level.Hole[p - w] ? p - w : p, d = y < h - 1 && !level.Hole[p + w] ? p + w : p;
                    across[p] = Math.Abs(brightness[r] - brightness[l]) / Math.Max(1, (r - l));
                    down[p] = Math.Abs(brightness[d] - brightness[u]) / Math.Max(1, (d - u) / w);
                    has[p] = true;
                }
            var (meanAcross, meanDown) = (BoxMean(across, has, w, h), BoxMean(down, has, w, h));
            for (int p = 0; p < w * h; p++)
            {
                if (level.Hole[p]) continue;
                level.Values[p * Channels + 3] = TextureWeight * meanAcross[p];
                level.Values[p * Channels + 4] = TextureWeight * meanDown[p];
            }
            return level;
        }

        /// <summary>Each pixel's mean of <paramref name="values"/> over the patch round it, counting where
        /// <paramref name="has"/>: rows, then columns, from running sums.</summary>
        private static float[] BoxMean(float[] values, bool[] has, int w, int h)
        {
            var sum = new float[w * h];
            var count = new int[w * h];
            for (int y = 0; y < h; y++)
            {
                float running = 0;
                int n = 0;
                for (int x = -Radius; x < w; x++)
                {
                    int add = x + Radius, drop = x - Radius - 1;
                    if (add < w && has[y * w + add]) (running, n) = (running + values[y * w + add], n + 1);
                    if (drop >= 0 && has[y * w + drop]) (running, n) = (running - values[y * w + drop], n - 1);
                    if (x >= 0) (sum[y * w + x], count[y * w + x]) = (running, n);
                }
            }
            var mean = new float[w * h];
            for (int x = 0; x < w; x++)
            {
                float running = 0;
                int n = 0;
                for (int y = -Radius; y < h; y++)
                {
                    int add = y + Radius, drop = y - Radius - 1;
                    if (add < h) (running, n) = (running + sum[add * w + x], n + count[add * w + x]);
                    if (drop >= 0) (running, n) = (running - sum[drop * w + x], n - count[drop * w + x]);
                    if (y >= 0) mean[y * w + x] = n > 0 ? running / n : 0;
                }
            }
            return mean;
        }

        private void Write(Level level)
        {
            for (int y = 0; y < box.Height; y++)
                for (int x = 0; x < box.Width; x++)
                {
                    int to = (box.Top + y) * pictureWidth + box.Left + x, from = y * box.Width + x;
                    if (!level.Hole[from]) continue;
                    for (int c = 0; c < 3; c++) rgba[to * 4 + c] = (byte)Math.Clamp(MathF.Round(level.Values[from * Channels + c]), 0, 255);
                }
        }

        /// <summary>
        /// The coarsest level's hole filled by Criminisi, Pérez and Toyama's exemplar fill: patch by patch from its edge, the
        /// patch filled next the one on the edge where the picture's lines run into the hole most strongly and the most of it is
        /// already known — its confidence times its data term — each taking the unknown pixels of the source patch most like it
        /// over its known ones, searched over every source (the level is small). So the first matches start from the shapes that
        /// run into the hole, a wall's edge carried across it, rather than from whatever bland patch matches a blend of its edge.
        /// Where the level has no whole patch to copy from, each pixel is the mean of the filled pixels beside it.
        /// </summary>
        private static void Seed(Level level)
        {
            int w = level.Width, h = level.Height;
            var sources = new Sources(level);
            var filled = level.Hole.Select(hole => !hole).ToArray();
            var confidence = filled.Select(f => f ? 1f : 0f).ToArray();
            if (sources.Count == 0)
            {
                Smear(level, filled);
                return;
            }
            var front = new HashSet<int>();
            for (int p = 0; p < filled.Length; p++)
                if (!filled[p] && Beside(level, filled, p).Any()) front.Add(p);
            while (front.Count > 0)
            {
                int next = -1;
                float highest = float.MinValue;
                foreach (int p in front)
                {
                    float priority = Confidence(level, confidence, p) * (Isophote(level, filled, p) + 1e-3f);
                    if (priority > highest) (highest, next) = (priority, p);
                }
                float patchConfidence = Confidence(level, confidence, next);
                var distances = new float[sources.Count];
                Parallel.For(0, sources.Count, i => distances[i] = level.Distance(next, sources.Centres[i], float.MaxValue, filled));
                int best = sources.Centres[Array.IndexOf(distances, distances.Min())];
                int nx = next % w, ny = next / w, bx = best % w, by = best / w;
                for (int dy = -Radius; dy <= Radius; dy++)
                    for (int dx = -Radius; dx <= Radius; dx++)
                    {
                        int x = nx + dx, y = ny + dy;
                        if ((uint)x >= (uint)w || (uint)y >= (uint)h) continue;
                        int q = y * w + x;
                        if (filled[q]) continue;
                        int from = ((by + dy) * w + bx + dx) * Channels;
                        for (int c = 0; c < Channels; c++) level.Values[q * Channels + c] = level.Values[from + c];
                        (filled[q], confidence[q]) = (true, patchConfidence);
                        front.Remove(q);
                    }
                for (int dy = -Radius - 1; dy <= Radius + 1; dy++)
                    for (int dx = -Radius - 1; dx <= Radius + 1; dx++)
                    {
                        int x = nx + dx, y = ny + dy;
                        if ((uint)x >= (uint)w || (uint)y >= (uint)h) continue;
                        int q = y * w + x;
                        if (!filled[q] && Beside(level, filled, q).Any()) front.Add(q);
                    }
            }
        }

        /// <summary>How much of the patch round <paramref name="p"/> is known, each pixel by how surely.</summary>
        private static float Confidence(Level level, float[] confidence, int p)
        {
            int x0 = p % level.Width, y0 = p / level.Width, n = 0;
            float sum = 0;
            for (int dy = -Radius; dy <= Radius; dy++)
                for (int dx = -Radius; dx <= Radius; dx++)
                {
                    int x = x0 + dx, y = y0 + dy;
                    if ((uint)x >= (uint)level.Width || (uint)y >= (uint)level.Height) continue;
                    sum += confidence[y * level.Width + x];
                    n++;
                }
            return sum / n;
        }

        /// <summary>How strongly the picture's lines run into the hole at <paramref name="p"/>: the brightness's isophote —
        /// its gradient turned a right angle, at the strongest known pixel nearby — across the hole's edge, its normal.</summary>
        private static float Isophote(Level level, bool[] filled, int p)
        {
            int w = level.Width, h = level.Height, x0 = p % w, y0 = p / w;
            float Brightness(int q) => 0.299f * level.Values[q * Channels] + 0.587f * level.Values[q * Channels + 1] + 0.114f * level.Values[q * Channels + 2];
            bool Known(int x, int y) => (uint)x < (uint)w && (uint)y < (uint)h && filled[y * w + x];

            // The edge's normal: the gradient of what is filled about p.
            float nx = 0, ny = 0;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                    if (Known(x0 + dx, y0 + dy)) (nx, ny) = (nx + dx, ny + dy);
            float length = MathF.Sqrt(nx * nx + ny * ny);
            if (length == 0) return 0;
            (nx, ny) = (nx / length, ny / length);

            // The strongest gradient among the known pixels round p whose neighbours either side are known too.
            float gx = 0, gy = 0, strongest = 0;
            for (int dy = -Radius; dy <= Radius; dy++)
                for (int dx = -Radius; dx <= Radius; dx++)
                {
                    int x = x0 + dx, y = y0 + dy;
                    if (!Known(x, y) || !Known(x - 1, y) || !Known(x + 1, y) || !Known(x, y - 1) || !Known(x, y + 1)) continue;
                    float ax = (Brightness(y * w + x + 1) - Brightness(y * w + x - 1)) / 2, ay = (Brightness((y + 1) * w + x) - Brightness((y - 1) * w + x)) / 2;
                    if (ax * ax + ay * ay > strongest) (strongest, gx, gy) = (ax * ax + ay * ay, ax, ay);
                }
            // The isophote is the gradient turned a right angle.
            return MathF.Abs(-gy * nx + gx * ny) / 255f;
        }

        /// <summary>The hole filled from its edge inwards, each pixel the mean of the filled pixels beside it.</summary>
        private static void Smear(Level level, bool[] filled)
        {
            var ring = new List<int>();
            Span<float> sum = stackalloc float[Channels];
            for (bool any = true; any;)
            {
                ring.Clear();
                for (int p = 0; p < filled.Length; p++)
                    if (!filled[p] && Beside(level, filled, p).Any()) ring.Add(p);
                foreach (int p in ring)
                {
                    int n = 0;
                    sum.Clear();
                    foreach (int q in Beside(level, filled, p))
                    {
                        for (int c = 0; c < Channels; c++) sum[c] += level.Values[q * Channels + c];
                        n++;
                    }
                    for (int c = 0; c < Channels; c++) level.Values[p * Channels + c] = sum[c] / n;
                }
                foreach (int p in ring) filled[p] = true;
                any = ring.Count > 0;
            }
        }

        /// <summary>The filled pixels of the eight beside <paramref name="p"/>.</summary>
        private static IEnumerable<int> Beside(Level level, bool[] filled, int p)
        {
            int x = p % level.Width, y = p / level.Width;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if ((dx | dy) == 0 || (uint)nx >= (uint)level.Width || (uint)ny >= (uint)level.Height) continue;
                    if (filled[ny * level.Width + nx]) yield return ny * level.Width + nx;
                }
        }

        /// <summary>A finer level's hole taken from the coarser one's, each pixel from the coarse pixel it falls in.</summary>
        private static void Spread(Level coarse, Level fine)
        {
            for (int y = 0; y < fine.Height; y++)
                for (int x = 0; x < fine.Width; x++)
                {
                    int p = y * fine.Width + x;
                    if (!fine.Hole[p]) continue;
                    int q = Math.Min(coarse.Height - 1, y / 2) * coarse.Width + Math.Min(coarse.Width - 1, x / 2);
                    for (int c = 0; c < Channels; c++) fine.Values[p * Channels + c] = coarse.Values[q * Channels + c];
                }
        }
    }

    /// <summary>The centres of the patches a level may copy from: wholly inside it, touching no hole.</summary>
    private sealed class Sources
    {
        public readonly bool[] Valid;
        public readonly int[] Centres;

        public Sources(Level level)
        {
            int w = level.Width, h = level.Height;
            // A hole anywhere in a patch rules its centre out: the hole grown by the patch's radius, rows then columns.
            var across = new bool[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    bool near = false;
                    for (int dx = -Radius; dx <= Radius && !near; dx++)
                    {
                        int nx = x + dx;
                        near = (uint)nx < (uint)w && level.Hole[y * w + nx];
                    }
                    across[y * w + x] = near;
                }
            Valid = new bool[w * h];
            var centres = new List<int>();
            for (int y = Radius; y < h - Radius; y++)
                for (int x = Radius; x < w - Radius; x++)
                {
                    bool near = false;
                    for (int dy = -Radius; dy <= Radius && !near; dy++) near = across[(y + dy) * w + x];
                    if (near) continue;
                    Valid[y * w + x] = true;
                    centres.Add(y * w + x);
                }
            Centres = [.. centres];
        }

        public int Count => Centres.Length;
    }

    /// <summary>
    /// For each patch overlapping the hole, the source patch most like it found so far, and how unlike they are — the
    /// nearest-neighbour field PatchMatch improves by trying each neighbour's source shifted and random sources about its
    /// own, ever closer.
    /// </summary>
    private sealed class Field
    {
        private readonly int _width, _height;
        private readonly int[] _targets;          // the centres of the patches overlapping the hole
        private readonly int[] _at;               // a pixel's index in _targets, or -1
        private readonly int[] _source;           // each target's source centre
        private readonly float[] _distance;       // each target's mean squared difference from its source

        private Field(Level level)
        {
            (_width, _height) = (level.Width, level.Height);
            _at = new int[_width * _height];
            Array.Fill(_at, -1);
            var targets = new List<int>();
            for (int y = 0; y < _height; y++)
                for (int x = 0; x < _width; x++)
                {
                    bool overlaps = false;
                    for (int dy = -Radius; dy <= Radius && !overlaps; dy++)
                        for (int dx = -Radius; dx <= Radius && !overlaps; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            overlaps = (uint)nx < (uint)_width && (uint)ny < (uint)_height && level.Hole[ny * _width + nx];
                        }
                    if (!overlaps) continue;
                    _at[y * _width + x] = targets.Count;
                    targets.Add(y * _width + x);
                }
            _targets = [.. targets];
            _source = new int[_targets.Length];
            _distance = new float[_targets.Length];
        }

        public static Field Random(Level level, Sources sources, int seed)
        {
            var field = new Field(level);
            var random = new Random(seed);
            for (int t = 0; t < field._targets.Length; t++)
            {
                field._source[t] = sources.Centres[random.Next(sources.Count)];
                field._distance[t] = level.Distance(field._targets[t], field._source[t], float.MaxValue);
            }
            return field;
        }

        /// <summary>This field at the next finer <paramref name="level"/>: each target's source the coarse source doubled and
        /// moved as the target is within its coarse pixel; random where that is no source there.</summary>
        public Field Doubled(Level level, Sources sources, int seed)
        {
            var fine = new Field(level);
            var random = new Random(seed);
            for (int t = 0; t < fine._targets.Length; t++)
            {
                int p = fine._targets[t], x = p % level.Width, y = p / level.Width;
                int cx = Math.Min(_width - 1, x / 2), cy = Math.Min(_height - 1, y / 2), coarse = _at[cy * _width + cx];
                int candidate = -1;
                if (coarse >= 0)
                {
                    int s = _source[coarse], sx = 2 * (s % _width) + (x - 2 * cx), sy = 2 * (s / _width) + (y - 2 * cy);
                    if ((uint)sx < (uint)level.Width && (uint)sy < (uint)level.Height && sources.Valid[sy * level.Width + sx]) candidate = sy * level.Width + sx;
                }
                fine._source[t] = candidate >= 0 ? candidate : sources.Centres[random.Next(sources.Count)];
                fine._distance[t] = level.Distance(p, fine._source[t], float.MaxValue);
            }
            return fine;
        }

        /// <summary>PatchMatch's rounds over every target: a row at a time in parallel, each row in turn forwards and
        /// backwards, trying the neighbours' sources shifted and random sources ever closer about its own.</summary>
        public void Match(Level level, Sources sources, int seed)
        {
            for (int t = 0; t < _targets.Length; t++) _distance[t] = level.Distance(_targets[t], _source[t], float.MaxValue);
            for (int round = 0; round < PatchMatchRounds; round++)
            {
                int step = round % 2 == 0 ? 1 : -1;
                // Rows run side by side, so the row above or below is read as it stood when the round began: what one row
                // finds never depends on how far another has got, and a fill is the same every time.
                var before = (int[])_source.Clone();
                Parallel.For(0, _height, y =>
                {
                    var random = new Random(HashCode.Combine(seed, round, y));
                    for (int i = 0; i < _width; i++)
                    {
                        int x = step > 0 ? i : _width - 1 - i, t = _at[y * _width + x];
                        if (t < 0) continue;
                        Improve(level, sources, before, t, x, y, step, random);
                    }
                });
            }
        }

        private void Improve(Level level, Sources sources, int[] before, int t, int x, int y, int step, Random random)
        {
            int p = _targets[t];
            // The neighbour before along the row, as this round has left it, and the one above or below, as the round found
            // it: their sources moved back by one.
            Try(level, sources, t, p, Shifted(_source, x - step, y, step, 0));
            Try(level, sources, t, p, Shifted(before, x, y - step, 0, step));
            for (int r = Math.Max(_width, _height); r >= 1; r /= 2)
            {
                int s = _source[t], sx = s % _width + random.Next(-r, r + 1), sy = s / _width + random.Next(-r, r + 1);
                if ((uint)sx < (uint)_width && (uint)sy < (uint)_height) Try(level, sources, t, p, sy * _width + sx);
            }
        }

        private int Shifted(int[] sources, int nx, int ny, int dx, int dy)
        {
            if ((uint)nx >= (uint)_width || (uint)ny >= (uint)_height) return -1;
            int n = _at[ny * _width + nx];
            if (n < 0) return -1;
            int s = sources[n], sx = s % _width + dx, sy = s / _width + dy;
            return (uint)sx < (uint)_width && (uint)sy < (uint)_height ? sy * _width + sx : -1;
        }

        private void Try(Level level, Sources sources, int t, int p, int candidate)
        {
            if (candidate < 0 || candidate == _source[t] || !sources.Valid[candidate]) return;
            float d = level.Distance(p, candidate, _distance[t]);
            if (d < _distance[t]) (_source[t], _distance[t]) = (candidate, d);
        }

        /// <summary>
        /// The fill remade from what the patches over each hole pixel copy there, as Image Melding remakes it (Darabi et al.):
        /// their colours, and their steps to the next pixel right and below, each averaged with a patch weighed the more the
        /// closer it matches — its distance against the field's 75th percentile, as Wexler et al. weigh — then the colours that
        /// take those steps, keep near those colours and meet the picture at the hole's edge (<see cref="Rebuild"/>). Averaged
        /// colours alone drag a fill towards whatever bland colour every patch agrees on; the steps carry the copied texture,
        /// and no seam, since every step comes from inside one patch.
        /// </summary>
        public void Vote(Level level)
        {
            if (_targets.Length == 0) return;
            var sorted = (float[])_distance.Clone();
            Array.Sort(sorted);
            float spread = Math.Max(1e-3f, sorted[(int)(0.75 * (sorted.Length - 1))]);
            var weight = new float[_targets.Length];
            for (int t = 0; t < weight.Length; t++) weight[t] = MathF.Exp(-_distance[t] / (2 * spread));

            int w = _width, h = _height;
            var values = level.Values;
            // A step is voted where it starts at a hole pixel, or at the known pixel left of or above one.
            var stepped = new bool[w * h];
            for (int q = 0; q < stepped.Length; q++)
                if (level.Hole[q])
                {
                    stepped[q] = true;
                    if (q % w > 0) stepped[q - 1] = true;
                    if (q >= w) stepped[q - w] = true;
                }
            var colour = new float[values.Length];
            var across = new float[w * h * 3];
            var down = new float[w * h * 3];
            Parallel.For(0, h, y =>
            {
                Span<float> sum = stackalloc float[Channels];
                Span<float> right = stackalloc float[3];
                Span<float> below = stackalloc float[3];
                for (int x = 0; x < w; x++)
                {
                    int q = y * w + x;
                    if (!stepped[q]) continue;
                    sum.Clear();
                    right.Clear();
                    below.Clear();
                    float total = 0, rightTotal = 0, belowTotal = 0;
                    for (int dy = -Radius; dy <= Radius; dy++)
                        for (int dx = -Radius; dx <= Radius; dx++)
                        {
                            int px = x - dx, py = y - dy;   // the patch centred there holds this pixel at (dx, dy)
                            if ((uint)px >= (uint)w || (uint)py >= (uint)h) continue;
                            int t = _at[py * w + px];
                            if (t < 0) continue;
                            int s = _source[t], sx = s % w + dx, sy = s / w + dy, from = (sy * w + sx) * Channels;
                            float wt = weight[t];
                            for (int c = 0; c < Channels; c++) sum[c] += wt * values[from + c];
                            total += wt;
                            if (sx + 1 < w)
                            {
                                for (int c = 0; c < 3; c++) right[c] += wt * (values[from + Channels + c] - values[from + c]);
                                rightTotal += wt;
                            }
                            if (sy + 1 < h)
                            {
                                for (int c = 0; c < 3; c++) below[c] += wt * (values[from + w * Channels + c] - values[from + c]);
                                belowTotal += wt;
                            }
                        }
                    for (int c = 0; c < Channels; c++) colour[q * Channels + c] = total > 0 ? sum[c] / total : values[q * Channels + c];
                    for (int c = 0; c < 3; c++)
                    {
                        across[q * 3 + c] = rightTotal > 0 ? right[c] / rightTotal : 0;
                        down[q * 3 + c] = belowTotal > 0 ? below[c] / belowTotal : 0;
                    }
                }
            });
            for (int q = 0; q < level.Hole.Length; q++)
                if (level.Hole[q])
                    for (int c = 3; c < Channels; c++) values[q * Channels + c] = colour[q * Channels + c];
            Rebuild(level, colour, across, down);
        }

        /// <summary>How much a hole pixel's voted colour holds it against the voted steps: small, so the steps — the texture —
        /// lead and the colours only keep the fill from drifting.</summary>
        private const float ColourHold = 0.2f;

        /// <summary>
        /// Each colour channel of the hole solved as a screened Poisson equation: the values whose steps to their neighbours
        /// are nearest the voted <paramref name="across"/> and <paramref name="down"/>, held to the voted
        /// <paramref name="colour"/> by <see cref="ColourHold"/>, the picture's own pixels round the hole fixed. Conjugate
        /// gradients from the voted colours, each equation divided by its diagonal.
        /// </summary>
        private static void Rebuild(Level level, float[] colour, float[] across, float[] down)
        {
            int w = level.Width, h = level.Height;
            var index = new int[w * h];
            var inside = new List<int>();
            for (int q = 0; q < index.Length; q++)
            {
                index[q] = level.Hole[q] ? inside.Count : -1;
                if (level.Hole[q]) inside.Add(q);
            }
            int n = inside.Count;
            var diagonal = new float[n];
            var around = new int[n * 4];
            Array.Fill(around, -1);
            for (int i = 0; i < n; i++)
            {
                int q = inside[i], x = q % w, y = q / w, k = 0;
                Span<int> beside = [x > 0 ? q - 1 : -1, x < w - 1 ? q + 1 : -1, y > 0 ? q - w : -1, y < h - 1 ? q + w : -1];
                diagonal[i] = ColourHold;
                foreach (int m in beside)
                {
                    if (m < 0) continue;
                    diagonal[i]++;
                    if (index[m] >= 0) around[i * 4 + k++] = index[m];
                }
            }

            var b = new double[n];
            var f = new double[n];
            for (int c = 0; c < 3; c++)
            {
                for (int i = 0; i < n; i++)
                {
                    int q = inside[i], x = q % w, y = q / w;
                    double rhs = ColourHold * colour[q * Channels + c];
                    // The step wanted from each neighbour to this pixel, and the neighbour's own value where it is the picture's.
                    if (x > 0) rhs += across[(q - 1) * 3 + c] + (index[q - 1] < 0 ? level.Values[(q - 1) * Channels + c] : 0);
                    if (x < w - 1) rhs += -across[q * 3 + c] + (index[q + 1] < 0 ? level.Values[(q + 1) * Channels + c] : 0);
                    if (y > 0) rhs += down[(q - w) * 3 + c] + (index[q - w] < 0 ? level.Values[(q - w) * Channels + c] : 0);
                    if (y < h - 1) rhs += -down[q * 3 + c] + (index[q + w] < 0 ? level.Values[(q + w) * Channels + c] : 0);
                    b[i] = rhs;
                    f[i] = colour[q * Channels + c];
                }
                Conjugate(f, b, diagonal, around);
                for (int i = 0; i < n; i++) level.Values[inside[i] * Channels + c] = (float)Math.Clamp(f[i], 0, 255);
            }
        }

        /// <summary>Solves  diagonal·x_i − Σ x_j (j around i) = b_i  for x by Jacobi-preconditioned conjugate gradients, from
        /// the x given, until each value's residual averages a hundredth of a level.</summary>
        private static void Conjugate(double[] x, double[] b, float[] diagonal, int[] around)
        {
            int n = x.Length;
            var r = new double[n];
            var z = new double[n];
            var d = new double[n];
            var ad = new double[n];
            void Apply(double[] v, double[] into)
                => Parallel.For(0, n, i =>
                {
                    double s = diagonal[i] * v[i];
                    for (int k = 0; k < 4; k++)
                        if (around[i * 4 + k] is var j and >= 0) s -= v[j];
                    into[i] = s;
                });
            Apply(x, ad);
            for (int i = 0; i < n; i++) (r[i], z[i]) = (b[i] - ad[i], (b[i] - ad[i]) / diagonal[i]);
            Array.Copy(z, d, n);
            double rz = 0, rr = 0;
            for (int i = 0; i < n; i++) (rz, rr) = (rz + r[i] * z[i], rr + r[i] * r[i]);
            double target = 1e-4 * n;
            for (int iteration = 0; iteration < 500 && rr > target; iteration++)
            {
                Apply(d, ad);
                double dad = 0;
                for (int i = 0; i < n; i++) dad += d[i] * ad[i];
                double alpha = rz / dad, next = 0;
                rr = 0;
                for (int i = 0; i < n; i++)
                {
                    (x[i], r[i]) = (x[i] + alpha * d[i], r[i] - alpha * ad[i]);
                    z[i] = r[i] / diagonal[i];
                    next += r[i] * z[i];
                    rr += r[i] * r[i];
                }
                double beta = next / rz;
                rz = next;
                for (int i = 0; i < n; i++) d[i] = z[i] + beta * d[i];
            }
        }
    }
}
