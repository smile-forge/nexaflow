using System.Text.Json.Serialization;

namespace Nexaflow.Features.Tabular.Templates;

/// <summary>How a saved template is offered when a tabular file is opened.</summary>
public enum TemplateScope
{
    /// <summary>Applied only when the user picks it from the Apply Template panel.</summary>
    Manual,
    /// <summary>Auto-applied to any compatible file opened from <see cref="TabularTemplate.FolderPath"/>.</summary>
    Folder,
    /// <summary>Auto-applied to any compatible file whose name matches <see cref="TabularTemplate.Glob"/>.</summary>
    Glob,
}

/// <summary>
/// A reusable, named reshaping of a delimited file: the <em>original</em> shape it was built from
/// (separator/quote/header/field-count/header-names — used to decide compatibility) plus the
/// <em>final</em> shape as the serialized transform chain (<see cref="TransformsJson"/>). Persisted in
/// <see cref="TabularTemplatesConfig"/> and round-tripped by System.Text.Json, so every member is a
/// public get/set POCO property.
/// </summary>
public sealed class TabularTemplate
{
    public string        Id    { get; set; } = string.Empty;
    public string        Name  { get; set; } = string.Empty;
    public TemplateScope Scope { get; set; } = TemplateScope.Manual;

    /// <summary>Exact folder this template auto-applies in (Folder scope only).</summary>
    public string FolderPath { get; set; } = string.Empty;
    /// <summary>File-name glob this template auto-applies to, e.g. <c>sales-*.csv</c> (Glob scope only).</summary>
    public string Glob       { get; set; } = string.Empty;

    // ── Original-shape signature (compatibility key) ──────────────────────────
    /// <summary>The detected separator as a one-char string, or null (single-column / fixed-width).</summary>
    public string?   Separator       { get; set; }
    public string?   Quote           { get; set; }
    public bool      HasHeader       { get; set; }
    public int       FieldCount      { get; set; }
    /// <summary>The original header cells (trimmed), present only when <see cref="HasHeader"/>.</summary>
    public string[]? OriginalHeaders { get; set; }

    // ── Final shape ───────────────────────────────────────────────────────────
    /// <summary>The serialized transform chain (see <c>TransformSerializer</c>).</summary>
    public string    TransformsJson { get; set; } = "[]";
    /// <summary>Denormalized effective column headers, for before/after display.</summary>
    public string[]? FinalHeaders   { get; set; }

    /// <summary>A short human-readable description of where this template applies.</summary>
    [JsonIgnore]
    public string ScopeSummary => Scope switch
    {
        TemplateScope.Folder => $"folder: {FolderPath}",
        TemplateScope.Glob   => $"name: {Glob}",
        _                    => "manual",
    };
}
