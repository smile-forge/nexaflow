using System.IO;
using System.Text.Json.Nodes;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Visuals.Text.Pages;

namespace Nexaflow.Core.Services.Tools;

/// <summary>
/// Page reading for the assistant, local or in the cloud alike: a picture file of a page — a scan, a photographed page, a
/// form — read on this machine into Markdown, its text returned; saved as a Markdown file too when asked, its figures in a
/// folder beside it and its diagrams drawn again by the workspace's image model. The picture is never touched.
/// </summary>
internal sealed class ReadPageFileTool(IPageReading reading, IAIService ai) : IClientTool
{
    public string Name => "read_page_file";

    public string Description =>
        "Reads a picture of a page of writing — a scan, a photographed page, a form, a receipt — with page reading on this " +
        "machine, and returns its text as Markdown: columns as one text, formulas as LaTeX, tables. A few seconds a page. " +
        "Give 'save' to keep it as a Markdown file, its figures beside it.";

    public IReadOnlyList<ClientToolParameter> Parameters =>
    [
        new("picture", "Full path of the picture: PNG, JPEG, WebP, BMP, TIFF, GIF or HEIC."),
        new("save", "A .md file path to save the text to, its figures in a folder beside it. Not saved when not given.", Required: false),
    ];

    public ToolSafety Safety => ToolSafety.SafeOperation;

    /// <summary>One at a time: each reads its page on the GPU or every CPU core.</summary>
    public bool Parallelizable => false;

    public async Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct)
    {
        if (reading.State is ToolModelsState.NotDownloaded or ToolModelsState.Downloading)
            return ToolResult.Error("page reading not downloaded",
                "Page reading is not on this machine yet: the user can download it in Settings > Local > Tools, or from the image viewer's Read page.");
        if (reading.State != ToolModelsState.Ready) return ToolResult.Error("page reading unavailable", "This machine cannot run page reading.");

        var path = ToolArgs.Str(arguments, "picture", "path_in", "source");
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) return ToolResult.Error("no picture", "Give the picture's full path as 'picture'.");
        if (!File.Exists(path)) return ToolResult.Error("no such picture", $"There is no file at '{path}'.");
        string extension = Path.GetExtension(path).ToLowerInvariant();
        if (!PictureFiles.Readable.Contains(extension)) return ToolResult.Error("not a picture", $"'{Path.GetFileName(path)}' is not a picture this tool reads.");
        var save = ToolArgs.Str(arguments, "save")?.Trim();
        if (save is { Length: > 0 } && (!Path.IsPathFullyQualified(save) || !save.EndsWith(".md", StringComparison.OrdinalIgnoreCase)))
            return ToolResult.Error("no such file", "Give 'save' as the full path of a .md file.");

        string scratch = Directory.CreateTempSubdirectory("nexaflow-page-").FullName;   // the page, and its figures when not saved
        string page = Path.Combine(scratch, "page.png");
        try
        {
            // The reader takes PNG and JPEG as they are; anything else is decoded, upright, and handed over as PNG.
            if (extension is ".png" or ".jpg" or ".jpeg") File.Copy(path, page);
            else
            {
                var (rgba, width, height) = await Task.Run(() => PictureFiles.Read(path), ct);
                await File.WriteAllBytesAsync(page, PictureFiles.Encode(rgba, width, height, ".png"), ct);
            }

            string? folder = save is { Length: > 0 } ? Path.GetDirectoryName(save)! : null;
            string figures = save is { Length: > 0 } ? Path.GetFileNameWithoutExtension(save) + ".figures" : "";
            var read = await new PageReader(reading, folder is null ? null : ai)
                .ReadAsync([page], folder is null ? scratch : Path.Combine(folder, figures), figures, null, ct);
            if (read.Markdown.Trim().Length == 0)
                return ToolResult.Ok($"No text was read from '{Path.GetFileName(path)}'.", "Page reading read no text from it: it may not be a page of writing.");
            if (save is null or "")
                return ToolResult.Ok($"Read '{Path.GetFileName(path)}'.", read.Pages[0]);
            Directory.CreateDirectory(folder!);
            await File.WriteAllTextAsync(save, read.Markdown, ct);
            return ToolResult.Ok($"Read '{Path.GetFileName(path)}' into '{Path.GetFileName(save)}'.", read.Markdown);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException)
        {
            return ToolResult.Error("page unreadable", $"'{Path.GetFileName(path)}' could not be read: {ex.Message}");
        }
        finally
        {
            try { Directory.Delete(scratch, recursive: true); } catch (IOException) { }
        }
    }
}
