using Nexaflow.Features.Common;
using Nexaflow.Features.WindowsFileSystem.Controls;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Nexaflow.Features.WindowsFileSystem.FileActions;

/// <summary>
/// A user-defined "create from template" entry. The template file itself is copied into
/// the feature's appdata template store on save; only its relative <see cref="TemplateFileName"/>
/// is persisted so the entry survives the original source file being moved or deleted.
/// </summary>
public sealed class TemplateDefinition
{
    public string Name          { get; set; } = string.Empty;
    public string Icon          { get; set; } = "📄";
    public string FileExtension { get; set; } = string.Empty;

    /// <summary>Relative name of the stored template inside the appdata templates dir.</summary>
    public string TemplateFileName { get; set; } = string.Empty;

    /// <summary>
    /// Transient: the source file the user picked in Options. Copied into the template store on
    /// <c>Apply</c>, then cleared. Never persisted.
    /// </summary>
    [JsonIgnore]
    public string SourcePath { get; set; } = string.Empty;
}

/// <summary>
/// Global feature config holding the user's "Templated Create" entries. Edited via
/// <see cref="TemplatedCreateEditorControl"/>; template files live alongside the config
/// JSON under <c>{base}\templatedcreate\templates</c>.
/// </summary>
[CustomControl(typeof(TemplatedCreateEditorControl))]
public sealed class TemplatedCreateConfig : IFeatureConfig
{
    public string ConfigName   => "templatedcreate";
    public string FriendlyName => "Templated Create";

    public List<TemplateDefinition> Templates { get; set; } = new();
}
