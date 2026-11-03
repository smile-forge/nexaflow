using System.Globalization;
using System.IO;
using System.Text.Json.Nodes;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.Images.Editing;
using Nexaflow.Features.Images.Editing.Recipe;
using Nexaflow.Features.Images.Editing.SceneDepth;
using Nexaflow.Features.Images.ViewModels;
using SkiaSharp;

namespace Nexaflow.Features.Images.ClientTools;

/// <summary>
/// The picture's scene depth for an assistant, made for a model rather than a person: things are named by phrase, never by
/// pixel — picture selection finds them — what comes back is a few lines of near, middle and far with boxes, never the depth
/// itself, and each action is one step of the picture's edits the user undoes at once. They work on the picture the Artist
/// tab shows — the photo, or the active picture layer — and nothing else.
/// </summary>
internal sealed class SceneDepthClientTools(ArtistViewModel artist, IShellServices? shell, ImageEditingServices editing)
{
    private const string Distances = "Distance runs from 0, the picture's nearest part, to 1, its farthest; boxes are [left, top, right, bottom] "
                                   + "in fractions of the picture's width and height.";

    public IReadOnlyList<IClientTool> All() =>
    [
        new DelegateClientTool(
            "describe_scene_depth",
            "How far things in the picture open in Artist lie from the camera, estimated on this machine from the picture alone: how much "
          + "of it is near, in the middle and far, and where; and, for each thing named in 'things', which of those it is in and how far "
          + "it lies. Read it before blurring or hazing by distance. " + Distances,
            [new ClientToolParameter("things", "Things to place, each in a few words: [\"the dog\", \"the house\"].", Required: false, Type: "array", ItemType: "string")],
            ToolSafety.SafeOperation,
            async (args, ct) => await DescribeAsync(args["things"] as JsonArray, ct),
            parallelizable: true),

        new DelegateClientTool(
            "blur_background",
            "Focus the picture on a thing and blur everything nearer and farther than it, as a wide-open lens does — the background "
          + "behind a subject softened, the subject kept sharp whole — as one step of its edits. 'focus' names the thing in a few words; "
          + "'strength' is how open the lens is, 0 to 100 (50), 0 taking the blur off. Answers with what it focused on and where.",
            [
                new ClientToolParameter("focus", "What to focus on, in a few words: \"the dog\", \"the woman in red\". Not needed to take the blur off.", Required: false),
                new ClientToolParameter("strength", "How strong the blur is, 0 to 100; 0 takes it off. 50 when not given.", Required: false, Type: "number"),
                new ClientToolParameter("shape", "The shape highlights out of focus take: round (the default) or hexagon.", Required: false, Choices: ["round", "hexagon"]),
                new ClientToolParameter("which", "Where 'focus' names several things, which to focus on, counting from the surest as describe_scene_depth "
                                               + "lists them. 1, the surest, when not given.", Required: false, Type: "integer"),
            ],
            ToolSafety.SafeOperation,
            async (args, ct) => await BlurAsync(ToolArgs.Str(args, "focus"), ToolArgs.Str(args, "strength"), ToolArgs.Str(args, "shape"),
                                                ToolArgs.IntOrNull(args, "which") ?? 1, ct)),

        new DelegateClientTool(
            "add_haze",
            "Lay haze over the picture that thickens with distance, as air does over far hills, as one step of its edits. 'density' is how "
          + "thick, 0 to 100 (40), 0 taking it off; 'start' how far off it begins, 0 to 100 of the picture's depth (25); 'colour' "
          + "#RRGGBB, or the colour of the picture's own distance when not given.",
            [
                new ClientToolParameter("density", "How thick, 0 to 100; 0 takes the haze off. 40 when not given.", Required: false, Type: "number"),
                new ClientToolParameter("start", "Where it begins, 0 (the nearest part) to 100 (the farthest). 25 when not given.", Required: false, Type: "number"),
                new ClientToolParameter("colour", "The haze's colour as #RRGGBB; the picture's own distance's colour when not given.", Required: false),
            ],
            ToolSafety.SafeOperation,
            async (args, ct) => await HazeAsync(ToolArgs.Str(args, "density"), ToolArgs.Str(args, "start"), ToolArgs.Str(args, "colour"), ct)),
    ];

    private Task OnUi(Action action) => shell is null ? RunHere(action) : shell.RunOnUiAsync(action);

    private async Task<T> OnUiAsync<T>(Func<T> read)
    {
        T value = default!;
        await OnUi(() => value = read());
        return value;
    }

    private static Task RunHere(Action action)
    {
        action();
        return Task.CompletedTask;
    }

    private static ToolResult NoPicture() => ToolResult.Error("no picture", "No photo or picture layer is open in this Artist tab to tell the depth of.");

    /// <summary>Why scene depth cannot be used — where to get it, when it can be had — or null when it is here.</summary>
    private ToolResult? DepthRefused() => editing.SceneDepth?.State switch
    {
        null or ToolModelsState.Unavailable => ToolResult.Error("scene depth unavailable", "This machine cannot run scene depth."),
        ToolModelsState.Ready => null,
        _ => ToolResult.Error("scene depth not downloaded",
                              "Scene depth (Depth Anything V2 Small) is not on this machine yet: the user can download it in Settings > Local > Tools, "
                            + "or from the editor's Adjust > Depth."),
    };

    /// <summary>The depth of the picture the tools work on, with its file's name; null with why where there is none.</summary>
    private async Task<(SceneDepthPlane Plane, SKSizeI Photo, string Name)?> DepthAsync()
    {
        var target = await OnUiAsync(() => artist.IsEditing && artist.EditTarget is not null ? artist.Depth.DepthOfTargetAsync() : null);
        if (target is null || await target is not var (plane, photo)) return null;
        return (plane, photo, await OnUiAsync(() => artist.CurrentFileName));
    }

    /// <summary>Everything <paramref name="phrase"/> names in the picture the tools work on, surest first; none where picture
    /// selection is not here.</summary>
    private async Task<IReadOnlyList<PictureFind>> FindAsync(string phrase, CancellationToken ct)
        => await (await OnUiAsync(() => artist.Cutter.ThingsNamedAsync(phrase, ct)));

    // ── describe_scene_depth ──────────────────────────────────────────────

    private async Task<ToolResult> DescribeAsync(JsonArray? named, CancellationToken ct)
    {
        if (DepthRefused() is { } refused) return refused;
        var phrases = named?.Select(n => n?.GetValue<string>()?.Trim()).OfType<string>().Where(p => p.Length > 0).Distinct().ToList() ?? [];
        if (phrases.Count > 0 && !artist.Cutter.CanPick)
            return ToolResult.Error("picture selection not downloaded",
                "Placing named things needs picture selection, which is not on this machine yet: the user can download it in Settings > Local > Tools. "
              + "Ask again without 'things' for the bands alone.");
        try
        {
            if (await DepthAsync() is not var (plane, photo, name)) return NoPicture();
            var things = new List<ThingDepth>();
            var unfound = new List<string>();
            foreach (var phrase in phrases)
            {
                var finds = await FindAsync(phrase, ct);
                if (finds.Count == 0) unfound.Add(phrase);
                foreach (var find in finds)
                    if (SceneDepthFocus.Of(find.Mask, plane, photo) is { } depth)
                        things.Add(new ThingDepth(phrase, find.Score, Fraction(find.Box, photo), depth));
            }
            var text = SceneDepthSummary.Describe(name, photo, SceneDepthSummary.Bands(plane), things, unfound);
            return ToolResult.Ok($"told the depth of {name}", text);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or InvalidDataException)
        {
            return ToolResult.Error("could not tell the depth", ex.Message);
        }
    }

    private static PictureBox Fraction(PictureBox box, SKSizeI photo)
        => new(box.Left / photo.Width, box.Top / photo.Height, box.Right / photo.Width, box.Bottom / photo.Height);

    // ── blur_background ───────────────────────────────────────────────────

    private async Task<ToolResult> BlurAsync(string? focus, string? strength, string? shape, int which, CancellationToken ct)
    {
        if (!TryPercent(strength, 50, out var open)) return ToolResult.Error("no such strength", "Give 'strength' as a number from 0 to 100.");
        var bokeh = shape?.Trim().ToLowerInvariant() switch
        {
            null or "" or "round" => BokehShape.Circle,
            "hexagon" => BokehShape.Hexagon,
            _ => (BokehShape?)null,
        };
        if (bokeh is not { } chosen) return ToolResult.Error("no such shape", "Give 'shape' as round or hexagon.");
        if (open == 0) return await TakeOffAsync(r => r.LensBlur is null ? null : r with { LensBlur = null }, "lens blur");

        focus = focus?.Trim();
        if (string.IsNullOrEmpty(focus)) return ToolResult.Error("nothing named", "Say what to focus on as 'focus'.");
        if (DepthRefused() is { } refused) return refused;
        if (!artist.Cutter.CanPick)
            return ToolResult.Error("picture selection not downloaded",
                "Finding what to focus on needs picture selection, which is not on this machine yet: the user can download it in Settings > Local > Tools.");
        try
        {
            if (await DepthAsync() is not var (plane, photo, name)) return NoPicture();
            var finds = await FindAsync(focus, ct);
            if (finds.Count == 0) return ToolResult.Ok($"found no {focus}", $"Nothing in '{name}' is \"{focus}\"; nothing was blurred.");
            if (which < 1 || which > finds.Count)
                return ToolResult.Error("no such one", $"'{name}' has {finds.Count} \"{focus}\"; give 'which' from 1 to {finds.Count}, or leave it out for the surest.");
            var subject = finds[which - 1];
            if (SceneDepthFocus.Of(subject.Mask, plane, photo) is not { } depth)
                return ToolResult.Ok($"found no {focus}", $"\"{focus}\" in '{name}' covers too little to focus on; nothing was blurred.");
            var blur = SceneDepthFocus.FocusedOn(new RecipeLensBlur { Aperture = open / 100, Shape = chosen }, depth);
            if (!await OnUiAsync(() => Commit(r => r with { LensBlur = blur })))
                return NoPicture();
            var box = Fraction(subject.Box, photo);
            return ToolResult.Ok($"blurred round {focus}", string.Create(CultureInfo.InvariantCulture,
                $"Focused '{name}' on \"{focus}\" ({subject.Score:0.00} sure) at [{box.Left:0.00}, {box.Top:0.00}, {box.Right:0.00}, {box.Bottom:0.00}], distance "
              + $"{depth.Distance:0.00}, kept sharp from {depth.Nearest:0.00} to {depth.Farthest:0.00}, and blurred what is nearer and farther at strength {open:0}"
              + $"{(finds.Count > 1 ? $" — number {which} of {finds.Count} found, surest first ('which' picks another)" : "")}, as one of its edits; the user can undo it or save it."));
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or InvalidDataException)
        {
            return ToolResult.Error("could not blur it", ex.Message);
        }
    }

    // ── add_haze ──────────────────────────────────────────────────────────

    private async Task<ToolResult> HazeAsync(string? density, string? start, string? colour, CancellationToken ct)
    {
        var fallback = new RecipeDepthHaze();
        if (!TryPercent(density, fallback.Density * 100, out var thick)) return ToolResult.Error("no such density", "Give 'density' as a number from 0 to 100.");
        if (!TryPercent(start, fallback.Start * 100, out var from)) return ToolResult.Error("no such start", "Give 'start' as a number from 0 to 100.");
        uint? argb = null;
        if (!string.IsNullOrWhiteSpace(colour))
        {
            if (!TryColour(colour, out var parsed)) return ToolResult.Error("no such colour", "Give 'colour' as #RRGGBB.");
            argb = parsed;
        }
        if (thick == 0) return await TakeOffAsync(r => r.DepthHaze is null ? null : r with { DepthHaze = null }, "haze");
        if (DepthRefused() is { } refused) return refused;
        try
        {
            if (await DepthAsync() is not var (_, _, name)) return NoPicture();
            ct.ThrowIfCancellationRequested();
            var haze = new RecipeDepthHaze { Density = thick / 100, Start = from / 100, Colour = argb };
            if (!await OnUiAsync(() => Commit(r => r with { DepthHaze = haze }))) return NoPicture();
            return ToolResult.Ok($"hazed {name}", string.Create(CultureInfo.InvariantCulture,
                $"Laid haze over '{name}' at density {thick:0}, starting {from:0}% of the way to its farthest part, "
              + $"{(argb is { } c ? $"coloured #{c & 0xFFFFFF:X6}" : "in the colour of its own distance")}, as one of its edits; the user can undo it or save it."));
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or InvalidDataException)
        {
            return ToolResult.Error("could not haze it", ex.Message);
        }
    }

    /// <summary>A depth effect taken off — one step, where the picture has it — and said so.</summary>
    private async Task<ToolResult> TakeOffAsync(Func<ImageRecipe, ImageRecipe?> without, string what)
    {
        string? name = null;
        bool had = false;
        bool open = await OnUiAsync(() =>
        {
            if (!artist.IsEditing || artist.EditTarget is not { } target) return false;
            name = artist.CurrentFileName;
            if (without(target.Recipe) is { } changed)
            {
                had = true;
                artist.Edit(_ => changed);
            }
            return true;
        });
        if (!open) return NoPicture();
        return ToolResult.Ok($"took off the {what}", had ? $"Took the {what} off '{name}' as one of its edits." : $"'{name}' has no {what} to take off.");
    }

    /// <summary>The recipe of the picture the tools work on changed as one step, Adjust shown; false where there is none.</summary>
    private bool Commit(Func<ImageRecipe, ImageRecipe> change)
    {
        if (!artist.IsEditing || artist.EditTarget is null) return false;
        artist.Editor.Tool = EditTool.Adjust;
        artist.Edit(change);
        return true;
    }

    private static bool TryPercent(string? text, double fallback, out double value)
    {
        value = fallback;
        if (string.IsNullOrWhiteSpace(text)) return true;
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) || value < 0 || value > 100) return false;
        return true;
    }

    private static bool TryColour(string text, out uint argb)
    {
        argb = 0;
        var hex = text.Trim().TrimStart('#');
        if (hex.Length != 6 || !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb)) return false;
        argb = 0xFF000000u | rgb;
        return true;
    }
}
