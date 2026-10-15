using System.IO;
using System.Text.Json.Nodes;
using Nexaflow.Core.Services.Generation;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Providers.Common;

namespace Nexaflow.Core.Services.Tools;

/// <summary>
/// The picture tools for the assistant, local or in the cloud alike: a picture file made twice or four times its size, or
/// kept at its size and cleaned, its faces repaired — saved as a new file beside it, or where the call asks, and opened.
/// The original is never touched.
/// </summary>
internal sealed class RestorePictureTool(IPictureTools tools, GeneratedFiles files) : IClientTool
{
    internal static readonly IReadOnlyList<string> Scales = ["1", "2", "4"];

    public string Name => "restore_picture";

    public string Description =>
        "Restores a picture with tools on this machine: makes it twice or four times its size, or cleans it at its size — " +
        "taking out softness, noise and JPEG blocks — and repairs the faces in it. Saves a new file and opens it; the original is kept.";

    public IReadOnlyList<ClientToolParameter> Parameters =>
    [
        new("picture", "Full path of the picture: PNG, JPEG, WebP, BMP, TIFF, GIF or HEIC."),
        new("scale", "How many times its size: 1 cleans it at its size. 2 when not given.", Required: false, Choices: Scales),
        new("faces", "Whether to repair the faces in it; yes when not given.", Required: false, Choices: ["yes", "no"]),
        new("path", "Where to save it: a folder, or a file path (.png or .jpg). Beside the original when not given.", Required: false),
    ];

    public ToolSafety Safety => ToolSafety.SafeOperation;

    /// <summary>One restoration at a time: each takes the GPU or every CPU core.</summary>
    public bool Parallelizable => false;

    public async Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct)
    {
        if (tools.State == ToolModelsState.NotDownloaded || tools.State == ToolModelsState.Downloading)
            return ToolResult.Error("picture tools not downloaded",
                "The picture tools are not on this machine yet: the user can download them in Settings > Local > Tools, or from the image editor's AI upscale.");
        if (tools.State != ToolModelsState.Ready)
            return ToolResult.Error("picture tools unavailable", "This machine cannot run the picture tools.");

        var path = ToolArgs.Str(arguments, "picture", "path_in", "source");
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) return ToolResult.Error("no picture", "Give the picture's full path as 'picture'.");
        if (!File.Exists(path)) return ToolResult.Error("no such picture", $"There is no file at '{path}'.");
        if (!PictureFiles.Readable.Contains(Path.GetExtension(path).ToLowerInvariant()))
            return ToolResult.Error("not a picture", $"'{Path.GetFileName(path)}' is not a picture this tool reads.");
        int scale = ToolArgs.Str(arguments, "scale") is { } s && int.TryParse(s, out var given) ? given : 2;
        if (scale is not (1 or 2 or 4)) return ToolResult.Error("no such scale", "The scale is 1, 2 or 4.");
        bool faces = !string.Equals(ToolArgs.Str(arguments, "faces"), "no", StringComparison.OrdinalIgnoreCase);

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
        if (!tools.Restores(width, height, scale))
            return ToolResult.Error("picture too large", $"A {width}x{height} picture is too large to restore at {scale}x; try a smaller scale.");

        var work = Directory.CreateTempSubdirectory("nexaflow-restore-").FullName;
        try
        {
            string input = Path.Combine(work, "picture.rgba"), output = Path.Combine(work, "restored.rgba");
            await File.WriteAllBytesAsync(input, rgba, ct);
            PictureRestoration restored;
            try
            {
                restored = await tools.RestoreAsync(new PictureRestoreRequest(input, width, height, scale, faces, output), null, ct);
            }
            catch (InvalidOperationException ex)
            {
                return ToolResult.Error("could not restore it", ex.Message);
            }

            var asked = ToolArgs.Str(arguments, "path");
            var extension = asked is { Length: > 0 } && Path.GetExtension(asked).ToLowerInvariant() is ".jpg" or ".jpeg" ? ".jpg" : ".png";
            var pixels = await File.ReadAllBytesAsync(output, ct);
            var encoded = await Task.Run(() => PictureFiles.Encode(pixels, restored.Width, restored.Height, extension), ct);
            var beside = Path.GetDirectoryName(path)! + Path.DirectorySeparatorChar;
            var saved = await files.KeepAsync(new MediaBytes(encoded, extension == ".jpg" ? "image/jpeg" : "image/png"), MadeKind.Picture,
                                              string.IsNullOrWhiteSpace(asked) ? beside : asked, $"{Path.GetFileNameWithoutExtension(path)} restored", ct);
            var facesSaid = !faces ? "" : restored.FacesRestored == 1 ? ", 1 face repaired" : $", {restored.FacesRestored} faces repaired";
            return ToolResult.Ok($"restored {Path.GetFileName(path)}",
                                 $"Saved to {saved} ({restored.Width}x{restored.Height}{facesSaid}) and opened it.") with { Attachments = [saved] };
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); } catch (IOException) { /* the temp folder's own clean-up has it */ }
        }
    }
}
