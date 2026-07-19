using System.Globalization;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.Model3D.ViewModels;

namespace Nexaflow.Features.Model3D.ClientTools;

/// <summary>
/// The AI tools the 3D viewer page exposes. All are read-only view operations (they move the camera or
/// read the model, never touch the file), so they auto-run. Camera moves and the viewport capture are
/// fulfilled by delegates the view wired onto the view-model; when the viewport isn't ready yet the tool
/// returns a friendly error rather than throwing. Tool invocations run on the UI sync context, so calling
/// those delegates directly is safe.
/// </summary>
internal static class Model3DTools
{
    public static IReadOnlyList<IClientTool> For(Model3DViewModel vm) =>
    [
        new DelegateClientTool(
            "capture_viewport_image",
            "Render the current 3D viewport and return it as an image so you can see the model.",
            [],
            ToolSafety.SafeOperation,
            (_, ct) => Capture(vm, ct),
            exemptFromRepeatGuard: true),

        new DelegateClientTool(
            "get_model_info",
            "Get exact triangle/vertex/mesh counts, the material list, and any file contents that aren't rendered.",
            [],
            ToolSafety.SafeOperation,
            (_, _) => Task.FromResult(ToolResult.Ok(
                $"{vm.FileName}: {vm.TriangleCount:N0} triangles, {vm.VertexCount:N0} vertices.", vm.DescribeInfo())),
            parallelizable: true),

        new DelegateClientTool(
            "orbit_model",
            "Rotate the camera around the model. 'yaw' turns left/right, 'pitch' tilts up/down — both in degrees.",
            [
                new ClientToolParameter("yaw", "Degrees to orbit horizontally, e.g. 90 or -45.", Required: false, Type: "number"),
                new ClientToolParameter("pitch", "Degrees to orbit vertically, e.g. 30 or -15.", Required: false, Type: "number"),
            ],
            ToolSafety.SafeOperation,
            (args, ct) => Orbit(vm, args, ct)),

        new DelegateClientTool(
            "roll_model",
            "Roll the camera about the line of sight (the third rotation axis). 'degrees' positive = the model turns clockwise as you look at it.",
            [new ClientToolParameter("degrees", "Degrees to roll, e.g. 90 (clockwise) or -45 (counter-clockwise).", Type: "number")],
            ToolSafety.SafeOperation,
            (args, ct) => Roll(vm, args, ct)),

        new DelegateClientTool(
            "zoom_model",
            "Zoom the camera. 'factor' > 1 zooms in (2 = twice as close); between 0 and 1 zooms out.",
            [new ClientToolParameter("factor", "Zoom factor, e.g. 2 to zoom in, 0.5 to zoom out.", Type: "number")],
            ToolSafety.SafeOperation,
            (args, ct) => Zoom(vm, args, ct)),

        new DelegateClientTool(
            "pan_model",
            "Pan the camera. 'dx' moves right/left, 'dy' moves up/down — each a fraction of the viewport (e.g. 0.25).",
            [
                new ClientToolParameter("dx", "Horizontal pan as a viewport fraction; right is positive.", Required: false, Type: "number"),
                new ClientToolParameter("dy", "Vertical pan as a viewport fraction; up is positive.", Required: false, Type: "number"),
            ],
            ToolSafety.SafeOperation,
            (args, ct) => Pan(vm, args, ct)),

        new DelegateClientTool(
            "reset_view",
            "Re-frame the model so the whole thing fits in the viewport.",
            [],
            ToolSafety.SafeOperation,
            (_, ct) => Reset(vm, ct)),

        new DelegateClientTool(
            "set_render_mode",
            "Switch between solid and wireframe rendering.",
            [new ClientToolParameter("wireframe", "true for wireframe, false for solid shaded.", Type: "boolean")],
            ToolSafety.SafeOperation,
            (args, _) => SetRenderMode(vm, args)),
    ];

    private static async Task<ToolResult> Capture(Model3DViewModel vm, CancellationToken ct)
    {
        if (vm.CaptureViewportPngAsync is null) return ToolResult.Error("The 3D viewport isn't ready to capture yet.");
        var png = await vm.CaptureViewportPngAsync(ct);
        if (png is not { Length: > 0 }) return ToolResult.Error("Couldn't render the viewport.");
        return ToolResult.Ok($"Captured the 3D viewport of {vm.FileName}.",
                "A screenshot of the current 3D viewport is attached for you to look at.")
            with { Images = [new ContextImage(png, "image/png", vm.FileName)] };
    }

    private static async Task<ToolResult> Orbit(Model3DViewModel vm, JsonObject args, CancellationToken ct)
    {
        if (vm.OrbitAsync is null) return ToolResult.Error("The 3D viewport isn't ready yet.");
        double yaw = Dbl(args, "yaw"), pitch = Dbl(args, "pitch");
        if (yaw == 0 && pitch == 0) return ToolResult.Error("Provide a non-zero 'yaw' and/or 'pitch' in degrees.");
        await vm.OrbitAsync(yaw, pitch, ct);
        return ToolResult.Ok($"Orbited the model (yaw {yaw:0.#}°, pitch {pitch:0.#}°). Capture the viewport to see it.");
    }

    private static async Task<ToolResult> Roll(Model3DViewModel vm, JsonObject args, CancellationToken ct)
    {
        if (vm.RollAsync is null) return ToolResult.Error("The 3D viewport isn't ready yet.");
        double degrees = Dbl(args, "degrees");
        if (degrees == 0) return ToolResult.Error("Provide a non-zero 'degrees' to roll.");
        await vm.RollAsync(degrees, ct);
        return ToolResult.Ok($"Rolled the view {Math.Abs(degrees):0.#}° {(degrees >= 0 ? "clockwise" : "counter-clockwise")}.");
    }

    private static async Task<ToolResult> Zoom(Model3DViewModel vm, JsonObject args, CancellationToken ct)
    {
        if (vm.ZoomAsync is null) return ToolResult.Error("The 3D viewport isn't ready yet.");
        double factor = Dbl(args, "factor");
        if (factor <= 0) return ToolResult.Error("Provide a positive 'factor' (>1 zooms in, <1 zooms out).");
        await vm.ZoomAsync(factor, ct);
        return ToolResult.Ok($"Zoomed by ×{factor:0.##}. Capture the viewport to see it.");
    }

    private static async Task<ToolResult> Pan(Model3DViewModel vm, JsonObject args, CancellationToken ct)
    {
        if (vm.PanAsync is null) return ToolResult.Error("The 3D viewport isn't ready yet.");
        double dx = Dbl(args, "dx"), dy = Dbl(args, "dy");
        if (dx == 0 && dy == 0) return ToolResult.Error("Provide a non-zero 'dx' and/or 'dy' (viewport fractions).");
        await vm.PanAsync(dx, dy, ct);
        return ToolResult.Ok($"Panned the camera (dx {dx:0.##}, dy {dy:0.##}).");
    }

    private static async Task<ToolResult> Reset(Model3DViewModel vm, CancellationToken ct)
    {
        if (vm.ResetViewAsync is null) return ToolResult.Error("The 3D viewport isn't ready yet.");
        await vm.ResetViewAsync(ct);
        return ToolResult.Ok("Re-framed the model to fit the viewport.");
    }

    private static Task<ToolResult> SetRenderMode(Model3DViewModel vm, JsonObject args)
    {
        bool wireframe = ToolArgs.Bool(args, "wireframe", !vm.ShowWireframe);
        vm.ShowWireframe = wireframe;
        return Task.FromResult(ToolResult.Ok($"Render mode set to {(wireframe ? "wireframe" : "solid")}."));
    }

    /// <summary>Tolerant double reader (JSON number or numeric string); 0 if absent/invalid.</summary>
    private static double Dbl(JsonObject args, string key)
    {
        if (args.TryGetPropertyValue(key, out var n) && n is JsonValue v)
        {
            if (v.TryGetValue<double>(out var d)) return d;
            if (v.TryGetValue<int>(out var i)) return i;
            if (v.TryGetValue<string>(out var s) &&
                double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var p)) return p;
        }
        return 0;
    }
}
