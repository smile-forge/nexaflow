using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Nexaflow.Core.Models;

namespace Nexaflow.Core.Services;

///<summary>
/// Persists and restores the ribbon layout for a single WorkContext to/from
/// <c>{contextDir}\ribbon.json</c>.
///
/// This class is intentionally a pure data layer — it only handles
/// serialisation of <see cref="RibbonItem"/> metadata (label, icon, kind,
/// page-kind, page-params).  Runtime <c>TabFactory</c> delegates are
/// re-attached by <c>ShellViewModel.ReattachTabFactory</c> after loading,
/// which is the single place that knows how to construct each page type.
/// </summary>
public sealed class RibbonLayoutService
{
    private readonly string _path;

    private static readonly JsonSerializerOptions _opts = new()
    {
        WriteIndented = true,
        Converters    = { new JsonStringEnumConverter() }
    };

    public RibbonLayoutService(string contextDir)
    {
        _path = Path.Combine(contextDir, "ribbon.json");
    }

    // ── Public API ────────────────────────────────────────────────────────

    public void Save(IEnumerable<RibbonItem> items)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var dtos = items.Select(ToDto).ToList();
        File.WriteAllText(_path, JsonSerializer.Serialize(dtos, _opts));
    }

    /// <summary>
    /// Returns <c>null</c> when no saved layout exists or the file is corrupt.
    /// <c>TabFactory</c> on each returned item will be <c>null</c> — the caller
    /// must invoke <c>ShellViewModel.ReattachTabFactory</c> before adding items
    /// to the ribbon.
    /// </summary>
    public List<RibbonItem>? Load()
    {
        if (!File.Exists(_path)) return null;
        try
        {
            var dtos = JsonSerializer.Deserialize<List<RibbonItemDto>>(
                           File.ReadAllText(_path), _opts);
            return dtos?.Select(FromDto).ToList();
        }
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
        try
        {
            var dtos = JsonSerializer.Deserialize<List<RibbonItemDto>>(File.ReadAllText(path), _opts);
            return dtos?.Select(dto => FromDto(ExpandTokens(dto))).ToList() ?? [];
        }
        catch { return []; }
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
        Kind        = item.Kind,
        Label       = item.Label,
        Icon        = item.Icon,
        IsHalf      = item.IsHalf,
        AccentColor = item.AccentColor,
        PageKind    = item.PageKind,
        PageParams  = item.PageParams,
        HalfItems   = item.HalfItems?.Select(ToDto).ToList()
    };

    private static RibbonItem FromDto(RibbonItemDto dto) => new()
    {
        Kind        = dto.Kind,
        Label       = dto.Label,
        Icon        = dto.Icon,
        IsHalf      = dto.IsHalf,
        AccentColor = dto.AccentColor,
        PageKind    = dto.PageKind,
        PageParams  = dto.PageParams,
        HalfItems   = dto.HalfItems?.Select(FromDto).ToList()
        // TabFactory intentionally left null — re-attached by ShellViewModel
    };
}

// ── DTO ───────────────────────────────────────────────────────────────────────

internal sealed class RibbonItemDto
{
    public RibbonItemKind              Kind        { get; set; } = RibbonItemKind.Button;
    public string                      Label       { get; set; } = string.Empty;
    public string                      Icon        { get; set; } = string.Empty;
    public bool                        IsHalf      { get; set; }
    public string?                     AccentColor { get; set; }
    public string?                     PageKind    { get; set; }
    public Dictionary<string, string>? PageParams  { get; set; }
    public List<RibbonItemDto>?        HalfItems   { get; set; }
}
