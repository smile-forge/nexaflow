using Nexaflow.Features.Common;
using Nexaflow.Features.WindowsFileSystem.FileActions;
using System.Collections.Generic;
using System.IO;

namespace Nexaflow.Features.WindowsFileSystem.Services;

/// <summary>
/// Singleton holding the live <see cref="TemplatedCreateConfig"/> snapshot and the base
/// directory for the template store (passed in from the host, since features can't reference
/// Core's <c>ConfigManager</c>). Mirrors <see cref="ExternalAppRegistry"/>; updated by
/// <c>TemplatedCreateEditorControl</c> on Save.
/// </summary>
public sealed class TemplatedCreateRegistry
{
    public static TemplatedCreateRegistry Instance { get; } = new();
    private TemplatedCreateRegistry() { }

    private TemplatedCreateConfig _config  = new();
    private string                _baseDir = string.Empty;

    /// <summary>Called once at startup with the config and the app's config base dir.</summary>
    public void Initialize(TemplatedCreateConfig config, string baseDir)
    {
        _config  = config;
        _baseDir = baseDir;
    }

    /// <summary>Replaces the held config after an Options Save.</summary>
    public void Update(TemplatedCreateConfig config) => _config = config;

    /// <summary>Directory where template files are stored, alongside the config JSON.</summary>
    public string TemplatesDir => Path.Combine(_baseDir, "templatedcreate", "templates");

    /// <summary>Fresh <see cref="IFileCreateAction"/>s for every configured template.</summary>
    public IReadOnlyList<IFileCreateAction> BuildCreateActions()
    {
        var dir  = TemplatesDir;
        var list = new List<IFileCreateAction>();
        foreach (var def in _config.Templates)
        {
            if (string.IsNullOrWhiteSpace(def.Name) || string.IsNullOrWhiteSpace(def.TemplateFileName))
                continue;
            list.Add(new TemplateCreateAction(
                def.Name, def.Icon, def.FileExtension, Path.Combine(dir, def.TemplateFileName)));
        }
        return list;
    }
}
