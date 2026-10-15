using System.Globalization;
using System.IO;
using System.Text.Json.Nodes;
using Nexaflow.Core.Services.Generation;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Providers.Common;

namespace Nexaflow.Core.Services.Tools;

/// <summary>
/// Picture selection for the assistant, local or in the cloud alike: what a phrase names in a picture file — or what a
/// box round it holds — cut out of its background into a new PNG beside it, transparent around it and cropped to it, and
/// opened. The original is never touched.
/// </summary>
internal sealed class CutOutTool(IPictureSelection selection, GeneratedFiles files) : IClientTool
{
    public string Name => "cut_out";

    public string Description =>
        "Cuts something out of a picture with a tool on this machine: everything 'what' names in it (\"the dog\", \"red car\"), or " +
        "the thing inside 'box'. Saves it as a new PNG — transparent around it, cropped to it — and opens it; the original is kept. " +
        "Says what it found, how surely and where.";

    public IReadOnlyList<ClientToolParameter> Parameters =>
    [
        new("picture", "Full path of the picture: PNG, JPEG, WebP, BMP, TIFF, GIF or HEIC."),
        new("what", "What to cut out, in a few words: a noun phrase like \"dog\" or \"woman in a yellow coat\". Not needed with 'box'.", Required: false),
        new("box", "A box round the one thing to cut out, in the picture's pixels: left,top,right,bottom.", Required: false),
        new("which", "With 'what': every one it names, or only the surest. Every one when not given.", Required: false, Choices: ["all", "surest"]),
        new("path", "Where to save it: a folder, or a .png file path. Beside the original when not given.", Required: false),
    ];

    public ToolSafety Safety => ToolSafety.SafeOperation;

    /// <summary>One at a time: each reads its picture on the GPU or every CPU core.</summary>
    public bool Parallelizable => false;

    public async Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct)
    {
        if (selection.State is ToolModelsState.NotDownloaded or ToolModelsState.Downloading)
            return ToolResult.Error("picture selection not downloaded",
                "Picture selection is not on this machine yet: the user can download it in Settings > Local > Tools, or from the image viewer's Select.");
        if (selection.State != ToolModelsState.Ready) return ToolResult.Error("picture selection unavailable", "This machine cannot run picture selection.");

        var path = ToolArgs.Str(arguments, "picture", "path_in", "source");
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) return ToolResult.Error("no picture", "Give the picture's full path as 'picture'.");
        if (!File.Exists(path)) return ToolResult.Error("no such picture", $"There is no file at '{path}'.");
        if (!PictureFiles.Readable.Contains(Path.GetExtension(path).ToLowerInvariant()))
            return ToolResult.Error("not a picture", $"'{Path.GetFileName(path)}' is not a picture this tool reads.");
        var what = ToolArgs.Str(arguments, "what")?.Trim();
        PictureBox? box = null;
        if (ToolArgs.Str(arguments, "box") is { Length: > 0 } given)
        {
            if (Box(given) is not { } parsed) return ToolResult.Error("no such box", "Give 'box' as left,top,right,bottom in the picture's pixels.");
            box = parsed;
        }
        if (string.IsNullOrEmpty(what) && box is null) return ToolResult.Error("nothing named", "Say what to cut out as 'what', or give a 'box' round it.");
        bool surest = string.Equals(ToolArgs.Str(arguments, "which"), "surest", StringComparison.OrdinalIgnoreCase);

        byte[] rgba;
        int width, height;
        try
        {
            (rgba, width, height) = await Task.Run(() => PictureFiles.Read(path), ct);
        }
        catch (Exception ex) when (ex is NotSupportedException or IOException or FileFormatException or InvalidOperationException)
        {
            return ToolResult.Error("picture unreadable", $"'{Path.GetFileName(path)}' could not be read: {ex.Message}");
        }

        List<PictureMask> masks;
        string said;
        try
        {
            await using var session = await selection.OpenAsync(rgba, width, height, ct);
            if (!string.IsNullOrEmpty(what))
            {
                var found = await session.FindAsync(what, box is null ? [] : [new PictureExample(box, Like: true)], width, height, ct);
                if (found.Count == 0) return ToolResult.Ok($"found no {what}", $"Nothing in {Path.GetFileName(path)} is \"{what}\"; nothing was saved.");
                var kept = surest ? found.Take(1).ToList() : [.. found];
                masks = [.. kept.Select(f => f.Mask)];
                said = string.Join("; ", kept.Select(f => $"{f.Score:P0} sure at {Describe(f.Box)}"));
            }
            else
            {
                var mask = await session.SelectAsync([], box, width, height, ct);
                if (mask.Bounds is null) return ToolResult.Ok("found nothing in the box", "Nothing stands out inside that box; nothing was saved.");
                masks = [mask];
                said = $"the thing at {Describe(mask.Bounds)}";
            }
        }
        catch (InvalidOperationException ex)
        {
            return ToolResult.Error("could not cut it out", ex.Message);
        }

        var (cut, cutWidth, cutHeight) = CutOut(rgba, width, height, masks);
        var asked = ToolArgs.Str(arguments, "path");
        var encoded = await Task.Run(() => PictureFiles.Encode(cut, cutWidth, cutHeight, ".png"), ct);
        var beside = Path.GetDirectoryName(path)! + Path.DirectorySeparatorChar;
        string name = $"{Path.GetFileNameWithoutExtension(path)} {(string.IsNullOrEmpty(what) ? "cut out" : what)}";
        var saved = await files.KeepAsync(new MediaBytes(encoded, "image/png"), MadeKind.Picture, string.IsNullOrWhiteSpace(asked) ? beside : asked, name, ct);
        return ToolResult.Ok($"cut out of {Path.GetFileName(path)}",
                             $"Cut out {(masks.Count == 1 ? "1 thing" : $"{masks.Count} things")} ({said}); saved to {saved} ({cutWidth}x{cutHeight}) and opened it.")
               with { Attachments = [saved] };
    }

    /// <summary>The picture where any of <paramref name="masks"/> picks it, transparent elsewhere, cropped to what is kept.</summary>
    internal static (byte[] Rgba, int Width, int Height) CutOut(byte[] rgba, int width, int height, IReadOnlyList<PictureMask> masks)
    {
        var keep = new bool[width * height];
        int left = width, top = height, right = -1, bottom = -1;
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int i = y * width + x;
                if (!masks.Any(m => m.Pixels[i] != 0)) continue;
                keep[i] = true;
                left = Math.Min(left, x);
                right = Math.Max(right, x);
                top = Math.Min(top, y);
                bottom = Math.Max(bottom, y);
            }
        if (right < 0) return ([0, 0, 0, 0], 1, 1);
        int w = right - left + 1, h = bottom - top + 1;
        var cut = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int from = (y + top) * width + x + left, to = (y * w + x) * 4;
                if (!keep[from]) continue;
                rgba.AsSpan(from * 4, 4).CopyTo(cut.AsSpan(to, 4));
            }
        return (cut, w, h);
    }

    private static PictureBox? Box(string text)
    {
        var parts = text.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 4) return null;
        var v = new double[4];
        for (int i = 0; i < 4; i++)
            if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out v[i])) return null;
        return v[2] > v[0] && v[3] > v[1] ? new PictureBox(v[0], v[1], v[2], v[3]) : null;
    }

    private static string Describe(PictureBox box)
        => string.Create(CultureInfo.InvariantCulture, $"{box.Left:F0},{box.Top:F0},{box.Right:F0},{box.Bottom:F0}");
}
