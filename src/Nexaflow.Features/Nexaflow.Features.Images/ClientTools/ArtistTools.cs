using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.Images.Editing;
using Nexaflow.Features.Images.Editing.Layers;
using Nexaflow.Features.Images.Editing.Recipe;
using Nexaflow.Features.Images.Editing.Rendering;
using Nexaflow.Features.Images.Editing.Selection;
using Nexaflow.Features.Images.ViewModels;
using Nexaflow.IO.Common;
using Nexaflow.Visuals.Common.Formatting;
using Nexaflow.Visuals.Common.Localization;
using Nexaflow.Visuals.Common.Theming;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Encodings.Web;

namespace Nexaflow.Features.Images.ClientTools;

/// <summary>
/// What the assistant can do in Artist, shaped for how a model works rather than after the buttons: <c>describe_picture</c> hands
/// it the whole state at once — the edits, every layer with an id, the selection, the history — and each tool that changes the
/// picture takes a description of what to make of it, applies it as one step the user can undo, and answers with the state it
/// left. Coordinates are one space throughout: a photo's own pixels, upright as taken, before its crop and turns; a layered
/// picture's canvas pixels. Nothing is written to disk — saving stays the user's.
/// </summary>
/// <remarks>The tools run off the UI thread, and every touch of the editor is marshalled onto it.</remarks>
internal sealed class ArtistTools(ArtistViewModel artist, IShellServices? shell, ImageEditingServices editing)
{
    /// <summary>The longest side the assistant is shown, in pixels.</summary>
    private const int CaptureEdge = 2048;

    private const string Coordinates =
        "Coordinates are a photo's own pixels, upright as taken, before its crop and turns — or, in a layered picture, the canvas's pixels.";

    /// <summary>The JSON the assistant reads: indented, its quotes and signs (×, —) as written rather than escaped.</summary>
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public IReadOnlyList<IClientTool> All() =>
    [
        new DelegateClientTool(
            "describe_picture",
            "Everything about the picture open in Artist, as JSON: its file, size and kind (a photo, or a layered picture); its edits — "
          + "adjustments, crop, turns, flips, straightening, size it saves at, restoration, removed background, erasures, annotations; "
          + "in a layered picture every layer top first with its id, name, kind, depth, visibility, opacity, blend, lock, clip, mask and "
          + "bounds, and which is active; the selection's bounds; and whether there is anything to undo or redo. Read it before changing "
          + "the picture, and the ids it gives are what the layer tools take. " + Coordinates,
            [],
            ToolSafety.SafeOperation,
            async (_, _) => await OnUiAsync(() => artist.IsStarting ? NoPicture() : ToolResult.Ok($"described {artist.CurrentFileName}", Describe().ToJsonString(Indented))),
            parallelizable: true),

        new DelegateClientTool(
            "capture_image",
            "See the picture: as the user sees it, edits and all — or, with 'box', one part of it at more detail, in the coordinates "
          + "the tools use (a photo's own pixels, so a box read off it can be handed straight to select_in_picture, crop or erase). "
          + "At most 2048 pixels on its longer side. " + Coordinates,
            [new ClientToolParameter("box", "The part to see, as left,top,right,bottom in the tools' coordinates. The whole picture as edited when not given.", Required: false)],
            ToolSafety.SafeOperation,
            async (args, _) => await CaptureAsync(ToolArgs.Str(args, "box")),
            exemptFromRepeatGuard: true) { Needs = [AiAbility.ImageRecognition] },

        new DelegateClientTool(
            "edit_picture",
            "Change the picture's edits — a photo's, or a layered picture's active picture layer's — in one step the user undoes at once. "
          + "Give only what should change; everything else stays. 'adjust' sets sliders (exposure -3..3 stops; contrast, highlights, "
          + "shadows, saturation, temperature, tint -100..100), after 'auto_enhance' where both are given. 'turn' is quarter turns "
          + "clockwise (negative anticlockwise), 'flip' horizontal or vertical, 'straighten' degrees. 'crop' is a box to keep, or "
          + "\"none\". 'resize' sets the size it saves at: {\"percent\": n} or {\"width\": w, \"height\": h} (one alone keeps its "
          + "proportions). 'annotate' adds annotations: [{\"kind\": \"arrow\"|\"box\"|\"text\", \"from\": [x, y], \"to\": [x, y], "
          + "\"text\": \"…\", \"colour\": \"#RRGGBB\"}]. 'background' is \"remove\", keeping what is selected, or \"keep\" to put it back. "
          + "Answers with the edits as they now stand. " + Coordinates,
            [
                new ClientToolParameter("adjust", "Slider values by name.", Required: false, Type: "object"),
                new ClientToolParameter("auto_enhance", "true to have the picture read and every slider set first.", Required: false, Type: "boolean"),
                new ClientToolParameter("turn", "Quarter turns clockwise, -3 to 3.", Required: false, Type: "integer"),
                new ClientToolParameter("flip", "Mirror it.", Required: false, Choices: ["horizontal", "vertical"]),
                new ClientToolParameter("straighten", $"Degrees, -{RecipeGeometry.MaxStraightenDegrees} to {RecipeGeometry.MaxStraightenDegrees}; the crop shrinks to keep empty corners out.", Required: false, Type: "number"),
                new ClientToolParameter("crop", "left,top,right,bottom to keep, or \"none\".", Required: false),
                new ClientToolParameter("resize", "{\"percent\": n} or {\"width\": w, \"height\": h}.", Required: false, Type: "object"),
                new ClientToolParameter("annotate", "Annotations to add.", Required: false, Type: "array", ItemType: "object"),
                new ClientToolParameter("background", "remove (keeping what is selected) or keep.", Required: false, Choices: ["remove", "keep"]),
            ],
            ToolSafety.SafeOperation,
            async (args, _) => await EditPictureAsync(args)),

        new DelegateClientTool(
            "select_in_picture",
            "Set the selection — what Cut out, Retouch, remove-background and, in a layered picture, fill, clear, masks and moves act on "
          + "— from any of: 'whole' (all, none or invert, first); 'find', everything a few words name, picked out by picture selection "
          + "(\"the mug\", \"woman with red hair\"); 'shapes', rectangles and ellipses [{\"shape\": \"rectangle\"|\"ellipse\", \"box\": "
          + "[l, t, r, b], \"mode\": \"replace\"|\"add\"|\"subtract\"|\"intersect\"}]; then 'refine' its edge in order [{\"how\": "
          + "\"feather\"|\"grow\"|\"shrink\"|\"smooth\", \"by\": pixels}]. 'mode' is how what 'find' picks meets what is selected. "
          + "Answers with what was found, how surely and where, and the selection's bounds. " + Coordinates,
            [
                new ClientToolParameter("whole", "all, none or invert, done first.", Required: false, Choices: ["all", "none", "invert"]),
                new ClientToolParameter("find", "What to pick out, in a few words: a noun phrase like \"dog\" or \"red car\".", Required: false),
                new ClientToolParameter("mode", "How what 'find' picks meets the selection: replace (the default), add, subtract or intersect.", Required: false,
                                        Choices: ["replace", "add", "subtract", "intersect"]),
                new ClientToolParameter("shapes", "Rectangles and ellipses, each with its box and mode.", Required: false, Type: "array", ItemType: "object"),
                new ClientToolParameter("refine", "Edge refinements, applied in order.", Required: false, Type: "array", ItemType: "object"),
            ],
            ToolSafety.SafeOperation,
            async (args, _) => await SelectAsync(args)),

        new DelegateClientTool(
            "edit_layers",
            "Change a picture's layers with a list of operations, applied in order as one step the user undoes at once — the photo made "
          + "a layered picture first where it is not one, kept apart from it until saved. A layer is named by its id from "
          + "describe_picture, or its name; left out, the active layer. Operations, each {\"op\": …}: "
          + "add {kind: pixels|notes|group, name?} (a group holds the active layer; the new layer is active); "
          + "set {layer?, name?, visible?, opacity? 0-100, blend?, locked?, clip?, keep_transparency?, active?}; "
          + "move {layer?, above|below|into: layer, or to: top|bottom}; duplicate {layer?}; delete {layer?}; merge_down {layer?}; "
          + "flatten {}; rasterize {layer?}; "
          + "transform {layer?, move: [dx, dy], scale: percent, rotate: degrees, flip: horizontal|vertical} about its middle; "
          + "fill {layer?, colour: #RRGGBB} and clear {layer?}, what is selected on a pixel layer or its mask; "
          + "mask {layer?, make: reveal_all|hide_all|from_selection|hiding_selection|from_transparency|invert|apply|delete|enable|disable}; "
          + "canvas {width?, height?, anchor?: top_left…bottom_right|centre} or canvas {to_selection: true}. "
          + "Blends: " + string.Join(", ", BlendNames.Keys) + ". Answers with each operation's outcome and the layers as they now stand.",
            [new ClientToolParameter("ops", "The operations, in order.", Type: "array", ItemType: "object")],
            ToolSafety.SafeOperation,
            async (args, _) => await EditLayersAsync(args["ops"] as JsonArray)),

        new DelegateClientTool(
            "generate_layer",
            "Draw a new layer with the workspace's image model, from a description, fitted to the canvas, above the active layer or the "
          + "one 'above' names — a background job, as generate_image is: it shows in the layers panel while it draws and arrives as a "
          + "layer. The photo is made a layered picture first where it is not one.",
            [
                new ClientToolParameter("description", "What to draw, as you would tell an illustrator."),
                new ClientToolParameter("above", "The layer it goes above, by id or name. The active layer when not given.", Required: false),
            ],
            ToolSafety.SafeOperation,
            async (args, _) => await GenerateLayerAsync(ToolArgs.Str(args, "description"), ToolArgs.Str(args, "above"))),

        new DelegateClientTool(
            "erase_in_picture",
            "Erase from the picture everything 'what' names — \"the sign\", \"people in the background\" — or the thing inside 'box', "
          + "and fill where it was from what is round it, as a step of its edits. Says what it erased and where. " + Coordinates,
            [
                new ClientToolParameter("what", "What to erase, in a few words. Not needed with 'box'.", Required: false),
                new ClientToolParameter("box", "A box round the one thing to erase: left,top,right,bottom.", Required: false),
                new ClientToolParameter("fill", "ai (LaMa, best where an edge or shape runs through it) or content-aware (the picture's own "
                                              + "texture). AI where it is downloaded, else content-aware.", Required: false, Choices: ["ai", "content-aware"]),
            ],
            ToolSafety.SafeOperation,
            async (args, ct) => await EraseAsync(ToolArgs.Str(args, "what"), ToolArgs.Str(args, "box"), ToolArgs.Str(args, "fill"), ct)),

        new DelegateClientTool(
            "restore_picture",
            "Restore the picture with the picture tools on this machine — softness, noise and JPEG blocks cleaned up, the faces in it "
          + "repaired — kept at its size or made twice or four times as large, as one of its edits (AI enhance and AI upscale).",
            [
                new ClientToolParameter("scale", "1 keeps its size, 2 or 4 makes it that many times as large. 1 when not given.", Required: false, Choices: ["1", "2", "4"]),
                new ClientToolParameter("faces", "Whether the faces in it are repaired. Yes when not given.", Required: false, Choices: ["yes", "no"]),
                new ClientToolParameter("strength", "How much of the restoration shows over the picture as it was, 0 to 100. 100 when not given.", Required: false, Type: "number"),
            ],
            ToolSafety.SafeOperation,
            async (args, ct) => await RestoreAsync(ToolArgs.Str(args, "scale"), ToolArgs.Str(args, "faces"), ToolArgs.Str(args, "strength"), ct)),

        new DelegateClientTool(
            "cut_out",
            "Cut something out of the picture — everything 'what' names, or the thing inside 'box' — into a new PNG, transparent "
          + "around it and cropped to it, opened for the user in a new Artist tab to save where they choose; the picture itself is "
          + "untouched. " + Coordinates,
            [
                new ClientToolParameter("what", "What to cut out, in a few words. Not needed with 'box'.", Required: false),
                new ClientToolParameter("box", "A box round the one thing to cut out: left,top,right,bottom.", Required: false),
            ],
            ToolSafety.SafeOperation,
            async (args, ct) => await CutOutAsync(ToolArgs.Str(args, "what"), ToolArgs.Str(args, "box"), ct)),

        new DelegateClientTool(
            "history",
            "Step through the picture's history — yours and the user's alike — as Undo and Redo do: 'undo' or 'redo' so many steps.",
            [
                new ClientToolParameter("undo", "Steps to take back.", Required: false, Type: "integer"),
                new ClientToolParameter("redo", "Steps to put back.", Required: false, Type: "integer"),
            ],
            ToolSafety.SafeOperation,
            async (args, _) => await HistoryAsync(ToolArgs.IntOrNull(args, "undo"), ToolArgs.IntOrNull(args, "redo"))),
    ];

    // ── Running on the editor ─────────────────────────────────────────────

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

    private static ToolResult NoPicture() => ToolResult.Error("no picture", "No picture is open in this Artist tab.");

    // ── describe_picture ──────────────────────────────────────────────────

    /// <summary>The picture's whole state, for the assistant.</summary>
    internal JsonObject Describe()
    {
        var document = artist.CurrentDocument;
        var state = new JsonObject
        {
            ["file"] = artist.CurrentFileName,
            ["format"] = Format(),
            ["kind"] = document is null ? "photo" : "layered",
            ["coordinates"] = document is null ? "the photo's own pixels, upright as taken, before its crop and turns" : "the canvas's pixels",
        };
        if (FileSize() is var bytes and >= 0) state["file_size"] = SizeFormatter.FormatBytes(bytes);
        if (document is { } layered)
        {
            var picture = layered.Picture;
            state["canvas"] = Size(picture.Width, picture.Height);
            state["dpi"] = picture.Dpi;
        }
        else if (artist.CurrentSession?.Proxy?.SourceSize is { } source) state["photo"] = Size((int)Math.Round(source.Width), (int)Math.Round(source.Height));
        if (artist.CurrentSession?.OutputSize(artist.CurrentRecipe) is { } output && document is null) state["saves_at"] = Size(output.Width, output.Height);
        if (artist.EditTarget is not null) state[document is null ? "edits" : "active_picture_layer_edits"] = Edits(artist.CurrentRecipe);
        if (document is not null) state["layers"] = Layers();
        state["selection"] = Selection();
        state["can_undo"] = artist.UndoEditCommand.CanExecute(null);
        state["can_redo"] = artist.RedoEditCommand.CanExecute(null);
        state["tool"] = ToolName(artist.Editor.Tool);
        return state;
    }

    /// <summary>A tool as the user knows it: Reshape for the tool the code calls Crop, the others by their own names.</summary>
    internal static string ToolName(EditTool tool) => tool == EditTool.Crop ? "reshape" : tool.ToString().ToLowerInvariant();

    private static JsonObject Size(int width, int height) => new() { ["width"] = width, ["height"] = height };

    private static JsonArray Box(double left, double top, double right, double bottom) =>
        [Math.Round(left), Math.Round(top), Math.Round(right), Math.Round(bottom)];

    private JsonObject Edits(ImageRecipe recipe)
    {
        var a = recipe.Adjustments;
        var g = recipe.Geometry;
        var edits = new JsonObject
        {
            ["adjust"] = new JsonObject
            {
                ["exposure"] = a.Exposure, ["contrast"] = a.Contrast, ["highlights"] = a.Highlights, ["shadows"] = a.Shadows,
                ["saturation"] = a.Saturation, ["temperature"] = a.Temperature, ["tint"] = a.Tint,
            },
            ["crop"] = g.Crop is { } c ? Box(c.X, c.Y, c.Right, c.Bottom) : null,
            ["quarter_turns"] = g.QuarterTurns,
            ["flip_horizontal"] = g.FlipHorizontal,
            ["flip_vertical"] = g.FlipVertical,
            ["straighten"] = Math.Round(g.StraightenDegrees, 1),
            ["resize"] = recipe.Resize is null ? null : artist.Editor.OutputSizeText,
            ["restore"] = recipe.Restore is { } r ? new JsonObject { ["scale"] = r.Scale, ["faces"] = r.Faces, ["strength"] = Math.Round(r.Strength * 100) } : null,
            ["background_removed"] = recipe.CutOut is not null,
            ["erasures"] = recipe.Erasures.Length,
            ["annotations"] = artist.MarkupTarget?.Markup.Length ?? recipe.Markup.Length,
        };
        return edits;
    }

    private JsonArray Layers()
    {
        var list = new JsonArray();
        if (artist.CurrentDocument is not { } document) return list;
        var picture = document.Picture;
        foreach (var item in artist.Layers.Items)
        {
            if (picture.All.FirstOrDefault(l => l.Id == item.Id) is not { } layer) continue;
            var entry = new JsonObject
            {
                ["id"] = ShortId(layer.Id),
                ["name"] = layer.Name,
                ["kind"] = item.Kind.ToString().ToLowerInvariant(),
                ["depth"] = item.Depth,
                ["visible"] = layer.Visible,
                ["opacity"] = Math.Round(layer.Opacity * 100),
                ["blend"] = BlendName(layer.Blend),
                ["locked"] = layer.Locked,
                ["clip"] = layer.ClipsToBelow,
                ["keep_transparency"] = layer.AlphaLocked,
                ["mask"] = layer.Mask is null ? "none" : layer.Mask.Enabled ? "on" : "off",
                ["active"] = item.IsActive,
            };
            if (artist.Arranging.BoundsOf(layer) is { IsEmpty: false } bounds) entry["bounds"] = Box(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
            list.Add(entry);
        }
        return list;
    }

    /// <summary>The selection's bounds and how much of the picture it covers, in the tools' coordinates; null where nothing is selected.</summary>
    private JsonObject? Selection()
    {
        if (artist.CurrentDocument is { } document)
        {
            var coverage = document.Picture.Selection;
            if (coverage.IsEmpty || LayerEdits.CoveredArea(coverage) is not { } area) return null;
            return new JsonObject { ["bounds"] = Box(area.Left, area.Top, area.Right, area.Bottom) };
        }
        var marks = artist.Editor.RegionMarks;
        if (marks.Shown is not { IsEmpty: false } shown || LayerEdits.CoveredArea(shown.Coverage) is not { } box || marks.ShownScale <= 0) return null;
        double scale = marks.ShownScale;
        return new JsonObject { ["bounds"] = Box(box.Left / scale, box.Top / scale, box.Right / scale, box.Bottom / scale) };
    }

    private string Format()
    {
        var ext = Path.GetExtension(artist.CurrentFileName);
        return string.IsNullOrEmpty(ext) ? "image" : ext.TrimStart('.').ToUpperInvariant();
    }

    [LocalDiskOnly("the size of a picture on this disk, for the assistant; none for one anywhere else")]
    private long FileSize()
    {
        if (artist.CurrentPath is not { } path) return -1;
        try { return File.Exists(path) ? new FileInfo(path).Length : -1; }
        catch { return -1; }
    }

    // ── capture_image ─────────────────────────────────────────────────────

    private async Task<ToolResult> CaptureAsync(string? box)
    {
        if (!TryBox(box, out var region)) return ToolResult.Error("no such box", "Give 'box' as left,top,right,bottom in the tools' coordinates.");
        var png = await (region is null ? EncodeAsEditedAsync() : EncodeRegionAsync(region));
        if (png is not { Length: > 0 })
            return ToolResult.Error("Couldn't capture the picture.", region is null ? "No picture is open, or it is not drawn yet." : "Nothing of the picture lies in that box.");
        var what = region is null ? $"'{artist.CurrentFileName}', as edited" : string.Create(CultureInfo.InvariantCulture, $"{region.Left:0},{region.Top:0},{region.Right:0},{region.Bottom:0} of '{artist.CurrentFileName}'");
        return ToolResult.Ok($"Captured {what}.", $"The picture {what} is attached for you to look at.")
               with { Images = [new ContextImage(png, "image/png", artist.CurrentFileName)] };
    }

    /// <summary>The picture as edited — a layered picture's composite, a photo through the screen's pipeline — at most
    /// <see cref="CaptureEdge"/> on its longer side.</summary>
    private async Task<byte[]?> EncodeAsEditedAsync()
    {
        if (artist.CurrentDocument is { } document)
        {
            var picture = document.Picture;
            using var renderer = await document.RendererForAsync(picture);
            using var composite = renderer?.Composite(Math.Min(1, (double)CaptureEdge / Math.Max(picture.Width, picture.Height)));
            using var encoded = composite?.Encode(SKEncodedImageFormat.Png, 100);
            return encoded?.ToArray();
        }
        if (artist.CurrentSession is not { } session || session.OutputSize(session.Recipe) is not { } output) return null;
        using var snapshot = session.Snapshot(session.Recipe, new RenderRequest(RenderFrame.Output, Math.Min(1, (double)CaptureEdge / Math.Max(output.Width, output.Height))));
        using var data = snapshot?.Encode(SKEncodedImageFormat.Png, 100);
        return data?.ToArray();
    }

    /// <summary>
    /// One part of the picture in the tools' coordinates: of a layered picture's composite, or of the photo in its own pixels with
    /// its adjustments — its crop, turns and size left out, so the part is where a box read off it says.
    /// </summary>
    private async Task<byte[]?> EncodeRegionAsync(PictureBox region)
    {
        double longer = Math.Max(region.Right - region.Left, region.Bottom - region.Top);
        double scale = Math.Min(1, CaptureEdge / longer);
        SKImage? whole;
        if (artist.CurrentDocument is { } document)
        {
            using var renderer = await document.RendererForAsync(document.Picture);
            whole = renderer?.Composite(scale);
        }
        else if (artist.CurrentSession is { } session)
        {
            var own = session.Recipe with { Geometry = RecipeGeometry.Identity, Resize = null, Markup = [] };
            whole = session.Snapshot(own, new RenderRequest(RenderFrame.Output, scale));
        }
        else return null;
        using (whole)
        {
            if (whole is null) return null;
            var part = SKRectI.Intersect(new SKRectI(0, 0, whole.Width, whole.Height),
                SKRectI.Round(new SKRect((float)(region.Left * scale), (float)(region.Top * scale), (float)(region.Right * scale), (float)(region.Bottom * scale))));
            if (part.IsEmpty) return null;
            using var cut = whole.Subset(part);
            using var data = cut?.Encode(SKEncodedImageFormat.Png, 100);
            return data?.ToArray();
        }
    }

    // ── edit_picture ──────────────────────────────────────────────────────

    private static readonly (string Key, double Min, double Max, Action<ImageEditorViewModel, double> Set)[] Sliders =
    [
        ("exposure", -3, 3, (e, v) => e.Exposure = v),
        ("contrast", -100, 100, (e, v) => e.Contrast = v),
        ("highlights", -100, 100, (e, v) => e.Highlights = v),
        ("shadows", -100, 100, (e, v) => e.Shadows = v),
        ("saturation", -100, 100, (e, v) => e.Saturation = v),
        ("temperature", -100, 100, (e, v) => e.Temperature = v),
        ("tint", -100, 100, (e, v) => e.Tint = v),
    ];

    /// <summary>What edit_picture was asked, read and checked whole before anything is changed.</summary>
    private sealed record PictureEdit(
        IReadOnlyList<(double Value, Action<ImageEditorViewModel, double> Set)> Sliders, bool Auto, int Turns, bool? FlipAcross,
        double? Straighten, PictureBox? Crop, bool Uncrop, double? Percent, int? Width, int? Height, IReadOnlyList<MarkupElement> Notes,
        string? Background);

    private async Task<ToolResult> EditPictureAsync(JsonObject args)
    {
        if (ReadEdit(args, out var edit) is { } refused) return refused;
        return await OnUiAsync(() =>
        {
            if (artist.EditTarget is null) return NoPicture();
            if (edit!.Notes.Count > 0 && artist.MarkupTarget is null)
                return ToolResult.Error("nowhere to annotate", "The active layer takes no annotations: make a picture or notes layer active, or add a notes layer with edit_layers.");
            var editor = artist.Editor;
            using (artist.Stage.Batch())
            {
                if (edit.Auto) editor.AutoEnhanceCommand.Execute(null);
                foreach (var (value, set) in edit.Sliders) set(editor, value);
                for (int i = 0; i < Math.Abs(edit.Turns); i++) artist.Edit(r => r with { Geometry = r.Geometry.Turned(Math.Sign(edit.Turns)) });
                if (edit.FlipAcross is { } across) artist.Edit(r => r with { Geometry = r.Geometry.Flipped(horizontal: across) });
                if (edit.Straighten is { } degrees) editor.Straighten = degrees;
                if (edit.Uncrop) artist.Edit(r => r with { Geometry = r.Geometry with { Crop = null } });
                else if (edit.Crop is { } box && artist.EditTarget?.Rendering.Proxy?.SourceSize is { } source)
                {
                    var wanted = new CropRect(box.Left, box.Top, box.Right - box.Left, box.Bottom - box.Top);
                    artist.Edit(r => r with { Geometry = r.Geometry with { Crop = CropGeometry.FitInside(source, r.Geometry.StraightenDegrees, wanted) } });
                }
                if (edit.Percent is { } percent) editor.ResizePercent = percent;
                else if (edit.Width is not null || edit.Height is not null)
                {
                    editor.ResizeByPixels = true;
                    editor.LockAspect = edit.Width is null || edit.Height is null;
                    if (edit.Width is { } w) editor.ResizeWidth = w;
                    if (edit.Height is { } h) editor.ResizeHeight = h;
                }
                if (edit.Notes.Count > 0) artist.MarkupTarget!.CommitMarkup(markup => markup.AddRange(edit.Notes));
                if (edit.Background == "remove")
                {
                    if (!editor.RemoveBackgroundCommand.CanExecute(null))
                        return ToolResult.Error("nothing to keep", "Removing the background keeps what is selected: select it first with select_in_picture.");
                    editor.RemoveBackgroundCommand.Execute(null);
                }
                else if (edit.Background == "keep") editor.KeepBackgroundCommand.Execute(null);
            }
            if (editor.IsCropTool && (edit.Crop is not null || edit.Uncrop)) editor.Tool = EditTool.Adjust;   // the crop shown applied, not its frame
            return ToolResult.Ok($"edited {artist.CurrentFileName}", Edits(artist.CurrentRecipe).ToJsonString(Indented));
        });
    }

    private ToolResult? ReadEdit(JsonObject args, out PictureEdit? edit)
    {
        edit = null;
        var sliders = new List<(double, Action<ImageEditorViewModel, double>)>();
        if (args["adjust"] is JsonObject adjust)
            foreach (var (name, node) in adjust)
            {
                int at = Array.FindIndex(Sliders, s => string.Equals(s.Key, name, StringComparison.OrdinalIgnoreCase));
                if (at < 0) return ToolResult.Error("no such slider", "The sliders are " + string.Join(", ", Sliders.Select(s => s.Key)) + ".");
                var (key, min, max, set) = Sliders[at];
                if (Number(node) is not { } value || value < min || value > max)
                    return ToolResult.Error($"no such {key}", string.Create(CultureInfo.InvariantCulture, $"{key} is from {min} to {max}."));
                sliders.Add((value, set));
            }
        bool auto = args["auto_enhance"] is JsonValue a && a.TryGetValue<bool>(out var autoValue) && autoValue;
        int turns = ToolArgs.IntOrNull(args, "turn") ?? 0;
        if (turns is < -3 or > 3) return ToolResult.Error("no such turn", "turn is from -3 to 3 quarter turns.");
        var flip = ToolArgs.Str(args, "flip");
        if (flip is { Length: > 0 } && flip is not ("horizontal" or "vertical")) return ToolResult.Error("no such flip", "flip is horizontal or vertical.");
        bool? across = flip switch { "horizontal" => true, "vertical" => false, _ => null };
        double? straighten = null;
        if (args["straighten"] is { } s)
        {
            if (Number(s) is not { } degrees || Math.Abs(degrees) > RecipeGeometry.MaxStraightenDegrees)
                return ToolResult.Error("no such angle", $"straighten is from -{RecipeGeometry.MaxStraightenDegrees} to {RecipeGeometry.MaxStraightenDegrees}.");
            straighten = degrees;
        }
        var cropNode = args["crop"];
        bool uncrop = cropNode is JsonValue named && named.TryGetValue<string>(out var word) && string.Equals(word, "none", StringComparison.OrdinalIgnoreCase);
        PictureBox? crop = null;
        if (!uncrop && !TryBox(cropNode, out crop)) return ToolResult.Error("no such crop", "Give crop as [left, top, right, bottom], or \"none\".");
        double? percent = null;
        int? width = null, height = null;
        if (args["resize"] is JsonObject resize)
        {
            if (resize["percent"] is { } p)
            {
                if (Number(p) is not { } value || value is < 1 or > 400) return ToolResult.Error("no such percentage", "resize.percent is from 1 to 400.");
                percent = value;
            }
            width = Number(resize["width"]) is { } w ? (int)Math.Round(w) : null;
            height = Number(resize["height"]) is { } h ? (int)Math.Round(h) : null;
            if (percent is null && width is null && height is null) return ToolResult.Error("no size", "resize takes percent, or width and/or height.");
            if (width is < 1 || height is < 1) return ToolResult.Error("no such size", "A width or height is 1 pixel or more.");
        }
        var notes = new List<MarkupElement>();
        if (args["annotate"] is JsonArray annotate)
            foreach (var node in annotate)
            {
                if (ReadNote(node as JsonObject, out var note) is { } bad) return bad;
                notes.Add(note!);
            }
        var background = ToolArgs.Str(args, "background");
        if (background is { Length: > 0 } && background is not ("remove" or "keep")) return ToolResult.Error("no such background", "background is remove or keep.");
        if (sliders.Count == 0 && !auto && turns == 0 && across is null && straighten is null && crop is null && !uncrop
            && percent is null && width is null && height is null && notes.Count == 0 && string.IsNullOrEmpty(background))
            return ToolResult.Error("nothing to edit", "Say what should change: adjust, auto_enhance, turn, flip, straighten, crop, resize, annotate or background.");
        edit = new PictureEdit(sliders, auto, turns, across, straighten, crop, uncrop, percent, width, height, notes, background);
        return null;
    }

    /// <summary>One annotation read: an arrow, a box or a line of text, in the editor's ink and weight unless coloured.</summary>
    private ToolResult? ReadNote(JsonObject? node, out MarkupElement? note)
    {
        note = null;
        if (node is null) return ToolResult.Error("no such annotation", "Each annotation is an object with its kind.");
        var kind = node["kind"]?.GetValue<string>();
        if (!TryPoint(node["from"], out var from)) return ToolResult.Error("no start", "Each annotation needs 'from' as [x, y].");
        uint? colour = null;
        if (node["colour"]?.GetValue<string>() is { Length: > 0 } text)
        {
            if (Colour(text) is not { } parsed) return ToolResult.Error("no such colour", "A colour is #RRGGBB.");
            colour = parsed;
        }
        uint argb = colour ?? SpecColours.Argb(artist.Editor.InkColour);
        double weight = artist.Editor.Weight;
        switch (kind)
        {
            case "arrow" or "box":
                if (!TryPoint(node["to"], out var to)) return ToolResult.Error("no end", $"A {kind} needs 'to' as [x, y].");
                note = kind == "arrow"
                    ? new MarkupArrow { From = from, To = to, Color = argb, StrokeWidth = weight }
                    : new MarkupBox { Corner = from, OppositeCorner = to, Color = argb, StrokeWidth = weight };
                return null;
            case "text":
                if (node["text"]?.GetValue<string>() is not { Length: > 0 } words || string.IsNullOrWhiteSpace(words))
                    return ToolResult.Error("no words", "Text needs its 'text'.");
                note = new MarkupText { Anchor = from, Text = words.Trim(), Color = argb };
                return null;
            default:
                return ToolResult.Error("no such annotation", "An annotation is an arrow, a box or text.");
        }
    }

    // ── select_in_picture ─────────────────────────────────────────────────

    private static RegionMode? Mode(string? text) => text switch
    {
        null or "" or "replace" or "new" => RegionMode.Replace,
        "add" => RegionMode.Add,
        "subtract" => RegionMode.Subtract,
        "intersect" => RegionMode.Intersect,
        _ => null,
    };

    private async Task<ToolResult> SelectAsync(JsonObject args)
    {
        var whole = ToolArgs.Str(args, "whole");
        if (whole is { Length: > 0 } && whole is not ("all" or "none" or "invert")) return ToolResult.Error("no such whole", "whole is all, none or invert.");
        var find = ToolArgs.Str(args, "find")?.Trim();
        if (Mode(ToolArgs.Str(args, "mode")) is not { } findMode) return ToolResult.Error("no such mode", "mode is replace, add, subtract or intersect.");
        var shapes = new List<RegionShape>();
        if (args["shapes"] is JsonArray given)
            foreach (var node in given)
            {
                if (node is not JsonObject shape || !TryBox(shape["box"], out var box) || box is null)
                    return ToolResult.Error("no such shape", "Each shape is {\"shape\": rectangle|ellipse, \"box\": [l, t, r, b], \"mode\": …}.");
                if (Mode(shape["mode"]?.GetValue<string>()) is not { } mode) return ToolResult.Error("no such mode", "A shape's mode is replace, add, subtract or intersect.");
                var kind = shape["shape"]?.GetValue<string>() ?? "rectangle";
                if (kind is not ("rectangle" or "ellipse")) return ToolResult.Error("no such shape", "A shape is a rectangle or an ellipse.");
                shapes.Add(kind == "ellipse"
                    ? new RegionEllipse(box.Left, box.Top, box.Right, box.Bottom) { Mode = mode }
                    : new RegionRectangle(box.Left, box.Top, box.Right, box.Bottom) { Mode = mode });
            }
        var refinements = new List<(string How, double By)>();
        if (args["refine"] is JsonArray refine)
            foreach (var node in refine)
            {
                var how = node?["how"]?.GetValue<string>();
                if (how is not ("feather" or "grow" or "shrink" or "smooth") || Number(node?["by"]) is not { } by || by is < 1 or > 250)
                    return ToolResult.Error("no such refinement", "Each refinement is {\"how\": feather|grow|shrink|smooth, \"by\": 1 to 250 pixels}.");
                refinements.Add((how, by));
            }
        if (string.IsNullOrEmpty(whole) && string.IsNullOrEmpty(find) && shapes.Count == 0 && refinements.Count == 0)
            return ToolResult.Error("nothing to select", "Give whole, find, shapes or refine.");
        if (!string.IsNullOrEmpty(find) && PickingRefused() is { } refused) return refused;

        // whole, then find — which waits on picture selection off the UI thread — then shapes and refinements.
        Task<IReadOnlyList<PictureFind>>? finding = null;
        var open = await OnUiAsync(() =>
        {
            if (!artist.IsEditing) return false;
            var editor = artist.Editor;
            if (!artist.IsLayered && !editor.TakesSelection) editor.Tool = EditTool.CutOut;   // a photo's selection is Cut out's and Retouch's
            var marks = editor.RegionMarks;
            switch (whole)
            {
                case "all": marks.SelectAllCommand.Execute(null); break;
                case "none": marks.DeselectCommand.Execute(null); break;
                case "invert": marks.InvertCommand.Execute(null); break;
            }
            if (!string.IsNullOrEmpty(find)) finding = artist.Cutter.FindNamedAsync(find);
            return true;
        });
        if (!open) return NoPicture();

        string found = "";
        if (finding is not null)
        {
            var finds = await finding;
            if (artist.Cutter.Failed) return ToolResult.Error("could not pick it out", artist.Cutter.Notice);
            found = finds.Count == 0
                ? $"Found no \"{find}\". "
                : $"Found {finds.Count} \"{find}\": " + string.Join("; ", finds.Select(f => string.Create(CultureInfo.InvariantCulture,
                      $"{f.Score:P0} sure at {f.Box.Left:F0},{f.Box.Top:F0},{f.Box.Right:F0},{f.Box.Bottom:F0}"))) + ". ";
        }
        return await OnUiAsync(() =>
        {
            var marks = artist.Editor.RegionMarks;
            if (finding is not null && artist.IsLayered) marks.TakePicked(findMode);
            foreach (var shape in shapes) marks.Add(shape);
            foreach (var (how, by) in refinements)
            {
                marks.RefineBy = by;
                (how switch { "feather" => marks.FeatherCommand, "grow" => marks.GrowCommand, "shrink" => marks.ShrinkCommand, _ => marks.SmoothCommand }).Execute(null);
            }
            var selection = Selection();
            return ToolResult.Ok("selection set", found + (selection is null
                ? "Nothing is selected now."
                : $"The selection{(artist.IsLayered ? ", on the canvas," : ", in the editor's Cut out,")} covers {selection["bounds"]!.ToJsonString()}."));
        });
    }

    private ToolResult? PickingRefused()
    {
        var cutter = artist.Cutter;
        if (!cutter.Available) return ToolResult.Error("picture selection unavailable", "This machine cannot run picture selection.");
        if (!cutter.CanPick)
            return ToolResult.Error("picture selection not downloaded",
                "Picture selection is not on this machine yet: the user can download it in Settings > Local > Tools, or from the editor's Cut out.");
        return null;
    }

    // ── edit_layers ───────────────────────────────────────────────────────

    /// <summary>The blends a layer takes, by the name the assistant uses: OpenRaster's operation without its prefix, Normal as "normal".</summary>
    private static readonly IReadOnlyDictionary<string, LayerBlend> BlendNames =
        LayerBlend.Offered.ToDictionary(b => b.Blend == LayerBlend.Normal ? "normal" : b.Blend.Op[(b.Blend.Op.IndexOf(':') + 1)..], b => b.Blend,
                                        StringComparer.OrdinalIgnoreCase);

    private static string BlendName(LayerBlend blend) => BlendNames.FirstOrDefault(n => n.Value == blend).Key ?? blend.Op;

    /// <summary>A layer's id as the assistant is given it: short, and unique in any picture a person makes.</summary>
    private static string ShortId(Guid id) => id.ToString("N")[..8];

    /// <summary>The layer a reference names — its id (whole or short), or its name — or the active one where there is none.</summary>
    private (LayerItem? Item, string? Why) Resolve(JsonNode? reference)
    {
        var items = artist.Layers.Items;
        var text = reference?.GetValue<string>()?.Trim();
        if (string.IsNullOrEmpty(text))
            return items.FirstOrDefault(i => i.IsActive) is { } active ? (active, null) : (null, "no layer is active");
        var byId = items.Where(i => i.Id.ToString("N").StartsWith(text.Replace("-", ""), StringComparison.OrdinalIgnoreCase)).ToList();
        if (byId.Count == 1) return (byId[0], null);
        var byName = items.Where(i => string.Equals(i.Name, text, StringComparison.OrdinalIgnoreCase)).ToList();
        return byName.Count switch
        {
            1 => (byName[0], null),
            0 => (null, $"no layer is '{text}'"),
            _ => (null, $"{byName.Count} layers are called '{text}': name it by its id"),
        };
    }

    /// <summary>Makes <paramref name="item"/> the active layer, as a click on its thumbnail does.</summary>
    private void Activate(LayerItem item)
    {
        if (!item.IsActive) artist.Layers.ActivateCommand.Execute(item);
    }

    private async Task<ToolResult> EditLayersAsync(JsonArray? ops)
    {
        if (ops is not { Count: > 0 }) return ToolResult.Error("no operations", "Give 'ops', a list of operations.");
        var made = await OnUiAsync(() => !artist.IsEditing ? "no picture" : artist.MakeLayered() is null ? "not ready" : null);
        if (made == "no picture") return NoPicture();
        if (made is not null) return ToolResult.Error("not ready", "The picture is not drawn yet; try again in a moment.");

        var outcomes = new JsonArray();
        IDisposable? step = await OnUiAsync(() => artist.Stage.Batch());
        try
        {
            for (int i = 0; i < ops.Count; i++)
            {
                if (ops[i] is not JsonObject op) return ToolResult.Error("no such operation", $"Operation {i + 1} is not an object.");
                var outcome = await ApplyAsync(op);
                outcomes.Add(new JsonObject { ["op"] = op["op"]?.DeepClone(), ["outcome"] = outcome.Text });
                if (!outcome.Done)
                {
                    // What was done before it stays one step; what was asked after it is not tried.
                    return ToolResult.Error($"operation {i + 1} could not be done", new JsonObject
                    {
                        ["stopped"] = $"at operation {i + 1}, which could not be done; the operations before it are kept, as one step, and those after it were not tried",
                        ["outcomes"] = outcomes,
                        ["layers"] = await OnUiAsync(Layers),
                    }.ToJsonString(Indented));
                }
            }
        }
        finally
        {
            await OnUi(() => step?.Dispose());
        }
        return ToolResult.Ok($"{ops.Count} layer operation(s)", new JsonObject
        {
            ["outcomes"] = outcomes,
            ["layers"] = await OnUiAsync(Layers),
            ["active"] = await OnUiAsync(() => artist.Layers.Items.FirstOrDefault(l => l.IsActive) is { } a ? ShortId(a.Id) : null),
        }.ToJsonString(Indented));
    }

    private readonly record struct Outcome(bool Done, string Text)
    {
        public static Outcome Ok(string text) => new(true, text);
        public static Outcome No(string text) => new(false, text);
    }

    /// <summary>One operation of edit_layers, through the layers panel's own controls; an async one awaited off the UI thread.</summary>
    private async Task<Outcome> ApplyAsync(JsonObject op)
    {
        Task? pending = null;
        var outcome = await OnUiAsync(() => Apply(op, out pending));
        if (pending is not null) await pending;
        return outcome;
    }

    private Outcome Apply(JsonObject op, out Task? pending)
    {
        pending = null;
        var layers = artist.Layers;
        var name = op["op"]?.GetValue<string>();
        LayerItem? Target(out string? why)
        {
            var (item, problem) = Resolve(op["layer"]);
            why = problem;
            if (item is not null) Activate(item);
            return item;
        }
        switch (name)
        {
            case "add":
            {
                var kind = op["kind"]?.GetValue<string>();
                CommunityToolkit.Mvvm.Input.IRelayCommand? command = kind switch
                {
                    "pixels" => layers.AddPixelLayerCommand,
                    "notes" => layers.AddMarkupLayerCommand,
                    "group" => layers.GroupCommand,
                    _ => null,
                };
                if (command is null) return Outcome.No("add takes kind pixels, notes or group");
                command.Execute(null);
                if (op["name"]?.GetValue<string>() is { Length: > 0 } given) layers.ActiveName = given;
                return Outcome.Ok($"added '{layers.ActiveName}' ({ShortId(layers.Items.First(i => i.IsActive).Id)})");
            }
            case "set":
            {
                if (Target(out var why) is not { } item) return Outcome.No(why!);
                if (op["name"]?.GetValue<string>() is { Length: > 0 } rename) layers.ActiveName = rename;
                if (Flag(op["locked"]) is { } locked) layers.ActiveLocked = locked;
                if (Flag(op["visible"]) is { } visible && item.Visible != visible) layers.ToggleVisibleCommand.Execute(item);
                if (op["opacity"] is { } o)
                {
                    if (Number(o) is not { } opacity || opacity is < 0 or > 100) return Outcome.No("opacity is from 0 to 100");
                    layers.Opacity = opacity;
                }
                if (op["blend"]?.GetValue<string>() is { Length: > 0 } blendName)
                {
                    if (!BlendNames.TryGetValue(blendName, out var blend)) return Outcome.No($"no blend is '{blendName}'");
                    layers.Blend = new LayerBlendChoice(blend, blendName);
                }
                if (Flag(op["clip"]) is { } clip) layers.ActiveClips = clip;
                if (Flag(op["keep_transparency"]) is { } keep) layers.ActiveAlphaLocked = keep;
                return Outcome.Ok($"set '{layers.ActiveName}'");
            }
            case "move":
            {
                if (Target(out var why) is not { } item) return Outcome.No(why!);
                var roots = layers.Items.Where(i => i.Depth == 0).ToList();
                (Guid Target, LayerDrop Drop)? where = op["to"]?.GetValue<string>() switch
                {
                    "top" => (roots[0].Id, LayerDrop.Above),
                    "bottom" => (roots[^1].Id, LayerDrop.Below),
                    _ => null,
                };
                foreach (var (key, drop) in new[] { ("above", LayerDrop.Above), ("below", LayerDrop.Below), ("into", LayerDrop.Into) })
                {
                    if (op[key] is not { } reference) continue;
                    var (other, problem) = Resolve(reference);
                    if (other is null) return Outcome.No(problem!);
                    where = (other.Id, drop);
                }
                if (where is not { } place) return Outcome.No("move takes above, below or into a layer, or to top or bottom");
                if (place.Target == item.Id) return Outcome.Ok("already there");
                layers.Move(item.Id, place.Target, place.Drop);
                return Outcome.Ok($"moved '{item.Name}'");
            }
            case "duplicate":
                if (Target(out var dupWhy) is null) return Outcome.No(dupWhy!);
                layers.DuplicateCommand.Execute(null);
                return Outcome.Ok($"duplicated as '{layers.ActiveName}' ({ShortId(layers.Items.First(i => i.IsActive).Id)})");
            case "delete":
            {
                if (Target(out var why) is not { } item) return Outcome.No(why!);
                if (!layers.DeleteCommand.CanExecute(null)) return Outcome.No("the last layer stays");
                layers.DeleteCommand.Execute(null);
                return Outcome.Ok($"deleted '{item.Name}'");
            }
            case "merge_down":
                if (Target(out var mergeWhy) is null) return Outcome.No(mergeWhy!);
                if (!layers.MergeDownCommand.CanExecute(null)) return Outcome.No("it has no unlocked layer beneath it to merge into");
                pending = layers.MergeDownCommand.ExecuteAsync(null);
                return Outcome.Ok("merged down");
            case "flatten":
                if (!layers.FlattenCommand.CanExecute(null)) return Outcome.No("there is only one layer");
                pending = layers.FlattenCommand.ExecuteAsync(null);
                return Outcome.Ok("flattened");
            case "rasterize":
                if (Target(out var rasterWhy) is null) return Outcome.No(rasterWhy!);
                if (!layers.RasterizeCommand.CanExecute(null)) return Outcome.No("only a picture or notes layer is rasterized");
                pending = layers.RasterizeCommand.ExecuteAsync(null);
                return Outcome.Ok("rasterized");
            case "transform":
            {
                if (Target(out var why) is not { } item) return Outcome.No(why!);
                double dx = 0, dy = 0, scale = 1, degrees = 0;
                if (op["move"] is { } by)
                {
                    if (!TryPoint(by, out var offset)) return Outcome.No("move is [dx, dy]");
                    (dx, dy) = (offset.X, offset.Y);
                }
                if (op["scale"] is { } s)
                {
                    if (Number(s) is not { } percent || percent is <= 0 or > 1000) return Outcome.No("scale is a percentage above 0, up to 1000");
                    scale = percent / 100;
                }
                if (op["rotate"] is { } r)
                {
                    if (Number(r) is not { } d) return Outcome.No("rotate is in degrees");
                    degrees = d;
                }
                var flip = op["flip"]?.GetValue<string>();
                if (flip is { Length: > 0 } && flip is not ("horizontal" or "vertical")) return Outcome.No("flip is horizontal or vertical");
                if (!artist.Arranging.TransformBy(scale * (flip == "horizontal" ? -1 : 1), scale * (flip == "vertical" ? -1 : 1), degrees,
                                                  new SKPoint((float)dx, (float)dy)))
                    return Outcome.No("it is locked, empty, or not one that moves");
                return Outcome.Ok($"transformed '{item.Name}'");
            }
            case "fill":
            {
                if (Target(out var why) is null) return Outcome.No(why!);
                if (op["colour"]?.GetValue<string>() is { Length: > 0 } text)
                {
                    if (Colour(text) is not { } argb) return Outcome.No("a colour is #RRGGBB");
                    artist.Editor.InkColour = ColorSpec.Custom(System.Windows.Media.Color.FromRgb((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));
                }
                if (!layers.FillCommand.CanExecute(null)) return Outcome.No("fill needs something selected on an unlocked pixel layer, or its mask");
                layers.FillCommand.Execute(null);
                return Outcome.Ok("filled");
            }
            case "clear":
                if (Target(out var clearWhy) is null) return Outcome.No(clearWhy!);
                if (!layers.DeleteSelectedCommand.CanExecute(null)) return Outcome.No("clear needs something selected on an unlocked pixel layer, or its mask");
                layers.DeleteSelectedCommand.Execute(null);
                return Outcome.Ok("cleared");
            case "mask":
            {
                if (Target(out var why) is not { } item) return Outcome.No(why!);
                var make = op["make"]?.GetValue<string>();
                if (make is "enable" or "disable")
                {
                    if (!item.HasMask) return Outcome.No("it has no mask");
                    if (item.MaskEnabled != (make == "enable")) layers.ToggleMaskEnabledCommand.Execute(item);
                    return Outcome.Ok($"mask {make}d");
                }
                var command = make switch
                {
                    "reveal_all" => layers.RevealAllMaskCommand,
                    "hide_all" => layers.HideAllMaskCommand,
                    "from_selection" => layers.MaskFromSelectionCommand,
                    "hiding_selection" => layers.MaskHidingSelectionCommand,
                    "from_transparency" => layers.MaskFromTransparencyCommand,
                    "invert" => layers.InvertMaskCommand,
                    "apply" => layers.ApplyMaskCommand,
                    "delete" => layers.DeleteMaskCommand,
                    _ => null,
                };
                if (command is null) return Outcome.No("mask takes make: reveal_all, hide_all, from_selection, hiding_selection, from_transparency, invert, apply, delete, enable or disable");
                if (!command.CanExecute(null))
                    return Outcome.No(make is "invert" or "apply" or "delete" ? "it has no mask to change" : "a layer has one mask: delete it first, or the selection it needs is empty");
                command.Execute(null);
                return Outcome.Ok($"mask {make.Replace('_', ' ')}");
            }
            case "canvas":
            {
                if (Flag(op["to_selection"]) == true)
                {
                    if (!layers.CropToSelectionCommand.CanExecute(null)) return Outcome.No("nothing is selected");
                    layers.CropToSelectionCommand.Execute(null);
                }
                else
                {
                    var picture = artist.CurrentDocument!.Picture;
                    layers.BeginCanvasSizeCommand.Execute(null);
                    layers.CanvasWidth = Number(op["width"]) is { } w ? (int)Math.Round(w) : picture.Width;
                    layers.CanvasHeight = Number(op["height"]) is { } h ? (int)Math.Round(h) : picture.Height;
                    var anchor = op["anchor"]?.GetValue<string>()?.Replace("_", "");
                    if (anchor is { Length: > 0 })
                    {
                        if (!Enum.TryParse<CanvasAnchor>(anchor, ignoreCase: true, out var at)) return Outcome.No("anchor is top_left, top, top_right, left, centre, right, bottom_left, bottom or bottom_right");
                        layers.CanvasAnchor = at;
                    }
                    if (!layers.ResizeCanvasCommand.CanExecute(null)) return Outcome.No(layers.CanvasMemory);
                    layers.ResizeCanvasCommand.Execute(null);
                }
                var now = artist.CurrentDocument!.Picture;
                return Outcome.Ok($"canvas {now.Width}×{now.Height}");
            }
            default:
                return Outcome.No($"no operation is '{name}'");
        }
    }

    // ── generate_layer ────────────────────────────────────────────────────

    private async Task<ToolResult> GenerateLayerAsync(string? description, string? above)
    {
        if (string.IsNullOrWhiteSpace(description)) return ToolResult.Error("nothing to draw", "Say what to draw as 'description'.");
        return await OnUiAsync(() =>
        {
            if (!artist.IsEditing) return NoPicture();
            var pictures = artist.Pictures;
            if (!pictures.OffersGeneration || pictures.Options is not { } options)
                return ToolResult.Error("no image model", "This workspace draws no pictures: the user can choose a model in its AI settings.");
            if (artist.MakeLayered() is not { } document) return ToolResult.Error("not ready", "The picture is not drawn yet; try again in a moment.");
            Guid? under = document.Picture.ActiveId;
            if (above is { Length: > 0 })
            {
                var (item, why) = Resolve(JsonValue.Create(above));
                if (item is null) return ToolResult.Error("no such layer", why!);
                under = item.Id;
            }
            options.Prefer(document.Picture.Width, document.Picture.Height);
            options.Prompt = description.Trim();
            if (!options.CanCreate) return ToolResult.Error("cannot draw yet", "The workspace's image model is not ready: it may still be downloading.");
            if (pictures.Draw(options) is not var (job, name)) return ToolResult.Error("could not begin", "The picture could not be begun; the user was told why.");
            pictures.Follow(job, name, document.Path, under);
            return ToolResult.Ok($"drawing '{name}'", $"Drawing \"{name}\" among the workspace's jobs; it shows in the layers panel while it draws and arrives as a layer.");
        });
    }

    // ── erase_in_picture, restore_picture, cut_out ────────────────────────

    /// <summary>restore_picture: the restore section of the recipe set, the tool showing it out, answered once it is made.</summary>
    private async Task<ToolResult> RestoreAsync(string? scale, string? faces, string? strength, CancellationToken ct)
    {
        int times = scale switch { null or "" or "1" => 1, "2" => 2, "4" => 4, _ => 0 };
        if (times == 0) return ToolResult.Error("no such scale", "The scale is 1, 2 or 4.");
        bool repair = !string.Equals(faces, "no", StringComparison.OrdinalIgnoreCase);
        double shown = 1;
        if (strength is { Length: > 0 })
        {
            if (Number(strength) is not { } percent || percent is < 0 or > 100)
                return ToolResult.Error("no such strength", "The strength is from 0 to 100.");
            shown = percent / 100;
        }
        var restorer = artist.Restorer;
        if (!restorer.Available) return ToolResult.Error("picture tools unavailable", "This machine cannot run the picture tools.");
        if (!restorer.RestoresNow)
            return ToolResult.Error("picture tools not downloaded",
                "The picture tools are not on this machine yet: the user can download them in Settings > Local > Tools, or from the editor's AI enhance.");

        var wanted = new RecipeRestore { Scale = times, Faces = repair, Strength = shown };
        string? refused = null;
        Task<string?>? made = null;
        bool already = false;
        await OnUi(() =>
        {
            if (artist.CurrentSession is not { Proxy: { } proxy } session)
            {
                refused = "No picture is open to restore.";
                return;
            }
            if (!restorer.Restores((int)Math.Round(proxy.SourceSize.Width), (int)Math.Round(proxy.SourceSize.Height), times))
            {
                refused = $"'{artist.CurrentFileName}' is too large to restore at {times}x; try a smaller scale.";
                return;
            }
            artist.Editor.Tool = times > 1 ? EditTool.Resize : EditTool.Adjust;
            if (session.Recipe.Restore == wanted && session.RestoredPath is not null)
            {
                already = true;
                return;
            }
            made = restorer.NextMadeAsync(ct);
            session.History.Commit(session.Recipe with { Restore = wanted });
        });
        if (refused is not null) return ToolResult.Error("could not restore it", refused);
        if (!already && await made! is { } failure) return ToolResult.Error("could not restore it", failure);
        return ToolResult.Ok($"restored {artist.CurrentFileName}",
            $"Restored '{artist.CurrentFileName}'{(times > 1 ? $" at {times}x its size" : "")}{(repair ? ", faces repaired" : "")}, shown at {shown:P0} strength, "
          + "as one of its edits; the user can undo it or save it.");
    }

    /// <summary>erase_in_picture: what is named — found first, so nothing is erased when nothing is — or what is boxed, erased as a
    /// step of the recipe with Retouch out; answered once it is made.</summary>
    private async Task<ToolResult> EraseAsync(string? what, string? box, string? fill, CancellationToken ct)
    {
        what = what?.Trim();
        if (!TryBox(box, out var boxed)) return ToolResult.Error("no such box", "Give 'box' as left,top,right,bottom.");
        if (string.IsNullOrEmpty(what) && boxed is null) return ToolResult.Error("nothing named", "Say what to erase as 'what', or give a 'box' round it.");
        if (PickingRefused() is { } refused) return refused;
        var eraser = artist.Eraser;
        var by = fill switch
        {
            null or "" => eraser.ErasesByAiNow ? EraseFill.Ai : EraseFill.ContentAware,
            _ when fill.Equals("ai", StringComparison.OrdinalIgnoreCase) => EraseFill.Ai,
            _ when fill.Equals("content-aware", StringComparison.OrdinalIgnoreCase) => EraseFill.ContentAware,
            _ => (EraseFill?)null,
        };
        if (by is not { } chosen) return ToolResult.Error("no such fill", "Fill with 'ai' or 'content-aware'.");
        if (chosen == EraseFill.Ai && !eraser.ErasesByAiNow)
            return ToolResult.Error("picture erasing not downloaded",
                "Picture erasing (LaMa) is not on this machine yet: the user can download it in Settings > Local > Tools, or erase with fill 'content-aware'.");

        Task<IReadOnlyList<PictureFind>>? finding = null;
        bool open = false;
        await OnUi(() =>
        {
            if (!artist.IsEditing) return;
            open = true;
            artist.Editor.Tool = EditTool.Retouch;
            if (!string.IsNullOrEmpty(what)) finding = artist.Cutter.FindNamedAsync(what);
        });
        if (!open) return NoPicture();

        string said;
        if (finding is not null)
        {
            var finds = await finding;
            if (artist.Cutter.Failed) return ToolResult.Error("could not find it", artist.Cutter.Notice);
            if (finds.Count == 0) return ToolResult.Ok($"found no {what}", $"Nothing in '{artist.CurrentFileName}' is \"{what}\"; nothing was erased.");
            said = $"{finds.Count} \"{what}\" at " + string.Join("; ", finds.Select(f => string.Create(CultureInfo.InvariantCulture,
                $"{f.Box.Left:F0},{f.Box.Top:F0},{f.Box.Right:F0},{f.Box.Bottom:F0}")));
        }
        else said = string.Create(CultureInfo.InvariantCulture, $"the thing in {boxed!.Left:F0},{boxed.Top:F0},{boxed.Right:F0},{boxed.Bottom:F0}");

        var step = new RecipeErase
        {
            Fill = chosen,
            Pick = !string.IsNullOrEmpty(what) ? new RecipeCutOut { Phrase = what } : new RecipeCutOut { Box = new RecipeCutBox(boxed!.Left, boxed.Top, boxed.Right, boxed.Bottom) },
        };
        var made = eraser.NextMadeAsync(ct);
        await OnUi(() =>
        {
            if (artist.CurrentSession is not { } session) return;
            session.History.Commit(session.Recipe with { Erasures = session.Recipe.Erasures.Add(step) });
            artist.Cutter.ClearCommand.Execute(null);
        });

        if (await made is { } failure) return ToolResult.Error("could not erase it", failure);
        return ToolResult.Ok($"erased {(string.IsNullOrEmpty(what) ? "the boxed thing" : what)}",
            $"Erased {said} from '{artist.CurrentFileName}', filled {(chosen == EraseFill.Ai ? "by AI" : "content-aware")}, as a step of its edits; the user can undo it or save it.");
    }

    /// <summary>
    /// cut_out: what is named, or boxed, picked as Cut out picks it and made a PNG — transparent around it, cropped to it — opened
    /// as a draft in an Artist tab of its own. The assistant is told only that the tab opened: the new picture is its to work on
    /// once the user gives it that tab.
    /// </summary>
    private async Task<ToolResult> CutOutAsync(string? what, string? box, CancellationToken ct)
    {
        what = what?.Trim();
        if (!TryBox(box, out var boxed)) return ToolResult.Error("no such box", "Give 'box' as left,top,right,bottom.");
        if (string.IsNullOrEmpty(what) && boxed is null) return ToolResult.Error("nothing named", "Say what to cut out as 'what', or give a 'box' round it.");
        if (PickingRefused() is { } refused) return refused;
        var cutter = artist.Cutter;

        Task<IReadOnlyList<PictureFind>>? finding = null;
        Task<bool>? picking = null;
        string? photo = null;
        await OnUi(() =>
        {
            if (artist.CurrentSession is not { } session) return;
            photo = session.Path;
            artist.Editor.Tool = EditTool.CutOut;
            if (!string.IsNullOrEmpty(what)) finding = cutter.FindNamedAsync(what);
            else picking = cutter.BoxedAsync(boxed!);
        });
        if (photo is null) return NoPicture();

        string said;
        if (finding is not null)
        {
            var finds = await finding;
            if (cutter.Failed) return ToolResult.Error("could not cut it out", cutter.Notice);
            if (finds.Count == 0) return ToolResult.Ok($"found no {what}", $"Nothing in '{artist.CurrentFileName}' is \"{what}\"; nothing was saved.");
            said = $"{finds.Count} \"{what}\"";
        }
        else
        {
            if (!await picking!) return cutter.Failed ? ToolResult.Error("could not cut it out", cutter.Notice)
                                                      : ToolResult.Ok("found nothing in the box", "Nothing stands out inside that box; nothing was saved.");
            said = "the thing in the box";
        }

        byte[]? png;
        try
        {
            png = await cutter.CutOutAsync(ct);
        }
        catch (InvalidOperationException ex)
        {
            return ToolResult.Error("could not cut it out", ex.Message);
        }
        if (png is null) return ToolResult.Ok("found nothing to keep", "What was picked has no pixels in this picture; nothing was saved.");

        // A draft: the tab gives the assistant the picture, never its folder, so nothing is written beside it. The user keeps the
        // cut-out where they choose, Save as starting beside the picture.
        var saved = DraftLocation.NewFile(Path.GetFileNameWithoutExtension(photo) + Str.Get("Images.CutOut.FileSuffix") + ".png");
        await editing.FileSystem.WriteAllBytesAsync(saved, png, ct);
        shell?.OpenTab(ArtistTabRegistration.StaticPageKind, new Dictionary<string, string>
        {
            ["path"]                      = saved,
            [DraftFile.PageParameterName] = ImageEditingServices.IsFileBacked(photo) ? Path.GetDirectoryName(photo) ?? "" : "",
        });
        return ToolResult.Ok($"cut out of {artist.CurrentFileName}",
            $"Cut out {said} from '{artist.CurrentFileName}' and opened it for the user in a new Artist tab, not saved yet: they save it where they choose.");
    }

    // ── history ───────────────────────────────────────────────────────────

    private async Task<ToolResult> HistoryAsync(int? undo, int? redo)
    {
        if (undo is null && redo is null) return ToolResult.Error("nothing to step", "Give undo or redo, a number of steps.");
        if (undo is < 1 or > 100 || redo is < 1 or > 100) return ToolResult.Error("no such count", "Steps are from 1 to 100.");
        return await OnUiAsync(() =>
        {
            if (!artist.IsEditing) return NoPicture();
            int undone = Step(artist.UndoEditCommand, undo ?? 0), redone = Step(artist.RedoEditCommand, redo ?? 0);
            return ToolResult.Ok($"undid {undone}, redid {redone}", $"Took back {undone} step(s) and put back {redone}; "
                + $"{(artist.UndoEditCommand.CanExecute(null) ? "more" : "nothing more")} to undo, {(artist.RedoEditCommand.CanExecute(null) ? "more" : "nothing more")} to redo.");
        });

        static int Step(CommunityToolkit.Mvvm.Input.IRelayCommand command, int steps)
        {
            int taken = 0;
            while (taken < steps && command.CanExecute(null))
            {
                command.Execute(null);
                taken++;
            }
            return taken;
        }
    }

    // ── Reading arguments ─────────────────────────────────────────────────

    /// <summary>A number however the JSON holds it — parsed from text, or made from an int, a long or a decimal — or a numeric string.</summary>
    private static double? Number(JsonNode? node) => node switch
    {
        JsonValue v when v.TryGetValue<double>(out var d) => double.IsFinite(d) ? d : null,
        JsonValue v when v.TryGetValue<int>(out var i) => i,
        JsonValue v when v.TryGetValue<long>(out var l) => l,
        JsonValue v when v.TryGetValue<decimal>(out var m) => (double)m,
        JsonValue v when v.TryGetValue<string>(out var s) => Number(s),
        _ => null,
    };

    private static double? Number(string? text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) ? value : null;

    private static bool? Flag(JsonNode? node) => node switch
    {
        JsonValue v when v.TryGetValue<bool>(out var b) => b,
        JsonValue v when v.TryGetValue<string>(out var s) => s.Trim().ToLowerInvariant() switch { "yes" or "true" => true, "no" or "false" => false, _ => null },
        _ => null,
    };

    /// <summary>#RRGGBB as opaque ARGB; null where it does not read.</summary>
    private static uint? Colour(string text)
    {
        var hex = text.Trim().TrimStart('#');
        return hex.Length == 6 && uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb) ? 0xFF000000 | rgb : null;
    }

    /// <summary>[x, y], or "x,y", read as a point; false where it does not read.</summary>
    private static bool TryPoint(JsonNode? node, out MarkupPoint point)
    {
        point = default;
        var values = Numbers(node);
        if (values is not { Length: 2 }) return false;
        point = new MarkupPoint(values[0], values[1]);
        return true;
    }

    /// <summary>A box — [l, t, r, b] or "l,t,r,b" — read; none given is a box of null, and false is one that does not read.</summary>
    private static bool TryBox(JsonNode? node, out PictureBox? box) => TryBox(Numbers(node), node is null, out box);

    private static bool TryBox(string? text, out PictureBox? box) =>
        TryBox(string.IsNullOrWhiteSpace(text) ? null : Numbers(JsonValue.Create(text)), string.IsNullOrWhiteSpace(text), out box);

    private static bool TryBox(double[]? values, bool absent, out PictureBox? box)
    {
        box = null;
        if (absent) return true;
        if (values is not { Length: 4 } v || v[2] <= v[0] || v[3] <= v[1]) return false;
        box = new PictureBox(v[0], v[1], v[2], v[3]);
        return true;
    }

    /// <summary>A JSON array of numbers, or a comma-separated string of them; null where it is neither.</summary>
    private static double[]? Numbers(JsonNode? node)
    {
        if (node is JsonArray array)
        {
            var values = array.Select(Number).ToArray();
            return values.All(v => v is not null) ? [.. values.Select(v => v!.Value)] : null;
        }
        if (node is JsonValue value && value.TryGetValue<string>(out var text))
        {
            var parts = text.Split(',', StringSplitOptions.TrimEntries);
            var values = parts.Select(Number).ToArray();
            return values.All(v => v is not null) ? [.. values.Select(v => v!.Value)] : null;
        }
        return null;
    }
}
