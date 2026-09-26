using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Nexaflow.Core.Models;
using Nexaflow.Icons;
using Nexaflow.Visuals.Common.Theming;
using Nexaflow.Visuals.Icons;

namespace Nexaflow.Core.Services;

///<summary>
/// Persists and restores the ribbon layout for a single Workspace to/from
/// <c>{workspaceDir}\ribbon.json</c>. One instance is shared by every WorkspaceRuntime on the workspace.
///
/// This class is intentionally a pure data layer — it only handles serialisation of
/// <see cref="RibbonItem"/> metadata (label, icon, look, kind, page-kind, page-params). Persisted items
/// carry no runtime open-behaviour: a button opens via its <c>PageKind</c>/<c>PageParams</c> routed
/// through the owning window's command, so loaded items need no per-window delegate fix-up.
/// <para>
/// The file is <c>{ "Version": 2, "Items": [...] }</c>. Version 1 was the bare item array, with the icon an emoji
/// and the one colour an <c>AccentColor</c> hex applied to the icon and label; it reads as the same items with
/// that colour as their <c>Foreground</c>, and is written back as version 2 on the next save. The icon and colour
/// strings need no mapping: <see cref="IconRef"/> reads a bare string as an emoji and <see cref="ColorSpec"/>
/// reads a hex as a custom colour.
/// </para>
/// </summary>
public sealed class RibbonLayoutService
{
    public const int CurrentVersion = 2;

    private readonly string _path;

    private static readonly JsonSerializerOptions _opts = new()
    {
        WriteIndented          = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        // A user's own file: keep emoji and accented labels readable rather than \u-escaped.
        Encoder                = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters             = { new JsonStringEnumConverter() }
    };

    public RibbonLayoutService(string workspaceDir)
    {
        _path = Path.Combine(workspaceDir, "ribbon.json");
    }

    // ── Public API ────────────────────────────────────────────────────────

    public void Save(IEnumerable<RibbonItem> items)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var layout = new RibbonLayoutDto { Version = CurrentVersion, Items = items.Select(ToDto).ToList() };
        File.WriteAllText(_path, JsonSerializer.Serialize(layout, _opts));
    }

    /// <summary>
    /// Returns <c>null</c> when no saved layout exists or the file is corrupt. Returned items are
    /// pure metadata (no runtime delegates); they open via PageKind through the owning window.
    /// </summary>
    public List<RibbonItem>? Load()
    {
        if (!File.Exists(_path)) return null;
        try { return Read(File.ReadAllText(_path))?.Select(FromDto).ToList(); }
        catch { return null; }
    }

    /// <summary>
    /// Loads the shipped <c>Ribbon/default-ribbon.json</c> and expands known-folder
    /// tokens (<c>{Documents}</c>, <c>{Pictures}</c>, <c>{Videos}</c>, <c>{Music}</c>)
    /// in PageParams values.  Returns an empty list if the file is missing or corrupt.
    /// </summary>
    public static List<RibbonItem> LoadDefaults()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Ribbon", "default-ribbon.json");
        if (!File.Exists(path)) return [];
        try { return Read(File.ReadAllText(path))?.Select(dto => FromDto(ExpandTokens(dto))).ToList() ?? []; }
        catch { return []; }
    }

    /// <summary>The items of either version: an array is version 1, an object carries its version and items.</summary>
    internal static List<RibbonItemDto>? Read(string json)
    {
        var root = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true });
        return root switch
        {
            JsonArray v1    => v1.Deserialize<List<RibbonItemDto>>(_opts),
            JsonObject file => file.Deserialize<RibbonLayoutDto>(_opts)?.Items,
            _               => null,
        };
    }

    private static RibbonItemDto ExpandTokens(RibbonItemDto dto)
    {
        if (dto.PageParams is null) return dto;
        dto.PageParams = dto.PageParams.ToDictionary(
            kv => kv.Key,
            kv => kv.Value switch
            {
                "{Documents}" => KnownFolderService.DocumentsPath,
                "{Pictures}"  => KnownFolderService.PicturesPath,
                "{Videos}"    => KnownFolderService.VideosPath,
                "{Music}"     => KnownFolderService.MusicPath,
                "{Desktop}"   => KnownFolderService.DesktopPath,
                "{Downloads}" => KnownFolderService.DownloadsPath,
                var v         => v
            });
        return dto;
    }

    // ── DTO mapping ───────────────────────────────────────────────────────

    private static RibbonItemDto ToDto(RibbonItem item) => new()
    {
        Kind         = item.Kind,
        Label        = item.Label,
        Icon         = item.Icon,
        IsHalf       = item.IsHalf,
        Foreground   = item.Foreground,
        Background   = item.Background,
        BorderColor  = item.BorderColor,
        BorderWeight = item.BorderWeight,
        Shape        = item.Shape,
        PageKind     = item.PageKind,
        PageParams   = item.PageParams,
        HalfItems    = item.HalfItems?.Select(ToDto).ToList()
    };

    private static RibbonItem FromDto(RibbonItemDto dto) => new()
    {
        Kind         = dto.Kind,
        Label        = dto.Label,
        Icon         = dto.Icon,
        IsHalf       = dto.IsHalf,
        // Version 1's one colour was the icon and label's.
        Foreground   = dto.Foreground.IsDefault ? ColorSpec.Parse(dto.AccentColor) : dto.Foreground,
        Background   = dto.Background,
        BorderColor  = dto.BorderColor,
        BorderWeight = dto.BorderWeight,
        Shape        = dto.Shape,
        PageKind     = dto.PageKind,
        PageParams   = dto.PageParams,
        HalfItems    = dto.HalfItems?.Select(FromDto).ToList()
        // TabFactory intentionally left null — persisted items open via PageKind.
    };
}

// ── DTO ───────────────────────────────────────────────────────────────────────

internal sealed class RibbonLayoutDto
{
    public int                 Version { get; set; }
    public List<RibbonItemDto> Items   { get; set; } = [];
}

internal sealed class RibbonItemDto
{
    public RibbonItemKind              Kind         { get; set; } = RibbonItemKind.Button;
    public string                      Label        { get; set; } = string.Empty;
    public IconRef                     Icon         { get; set; }
    public bool                        IsHalf       { get; set; }
    public ColorSpec                   Foreground   { get; set; }
    public ColorSpec                   Background   { get; set; }
    public ColorSpec                   BorderColor  { get; set; }
    public RibbonBorderWeight          BorderWeight { get; set; }
    public RibbonButtonShape           Shape        { get; set; }

    /// <summary>Version 1's colour, read only — <see cref="Foreground"/> replaces it.</summary>
    public string?                     AccentColor  { get; set; }

    public string?                     PageKind     { get; set; }
    public Dictionary<string, string>? PageParams   { get; set; }
    public List<RibbonItemDto>?        HalfItems    { get; set; }
}
