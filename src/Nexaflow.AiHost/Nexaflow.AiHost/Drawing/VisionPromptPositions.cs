namespace Nexaflow.AiHost.Drawing;

/// <summary>
/// A prompt's rotary positions, [3, tokens], as Qwen's vision-language models number a prompt with pictures in it
/// (<c>get_rope_index</c>): its text on the diagonal; each picture's run of placeholders the grid of its merged patches,
/// (p, p + row, p + column) from the position p it is reached at; the text after it numbered on from one past the grid's
/// largest. Qwen2.5-VL's prompt encoder and HiDream-O1's denoiser, a Qwen3-VL, number their prompts so.
/// </summary>
internal static class VisionPromptPositions
{
    /// <param name="ids">The prompt's tokens, each picture's placeholders a run of <paramref name="imageToken"/>.</param>
    /// <param name="grids">Each picture's merged grid, in the prompt's order.</param>
    /// <param name="trailing">Tokens after the prompt numbered on the diagonal with it — HiDream's timestep token.</param>
    public static long[] Of(ReadOnlySpan<long> ids, long imageToken, IReadOnlyList<(int Rows, int Cols)> grids, int trailing = 0)
    {
        int count = ids.Length + trailing;
        var positions = new long[3 * count];
        long next = 0;
        int picture = 0, at = 0;
        while (at < ids.Length)
        {
            if (ids[at] != imageToken)
            {
                positions[at] = positions[count + at] = positions[2 * count + at] = next++;
                at++;
                continue;
            }
            if (picture >= grids.Count) throw new ArgumentException("The prompt holds more pictures than it is given grids for.", nameof(grids));
            var (rows, cols) = grids[picture++];
            for (int i = 0; i < rows * cols; i++)
                if (at + i >= ids.Length || ids[at + i] != imageToken)
                    throw new ArgumentException("A picture's placeholders are not its grid's size.", nameof(ids));
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++, at++)
                {
                    positions[at] = next;
                    positions[count + at] = next + r;
                    positions[2 * count + at] = next + c;
                }
            next += Math.Max(rows, cols);
        }
        if (picture != grids.Count) throw new ArgumentException("The prompt holds fewer pictures than it is given grids for.", nameof(grids));
        for (int i = ids.Length; i < count; i++) positions[i] = positions[count + i] = positions[2 * count + i] = next++;
        return positions;
    }
}
