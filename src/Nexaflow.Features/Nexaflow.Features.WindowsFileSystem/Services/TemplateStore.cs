using Nexaflow.Features.WindowsFileSystem.FileActions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Nexaflow.Features.WindowsFileSystem.Services;

/// <summary>
/// Pure (WPF-free) persistence for templated-create source files: copies each row's picked
/// source file into <paramref name="templatesDir"/>, records the stored relative name on the
/// definition, and prunes orphaned files no definition references. Unit-testable against a
/// temp directory.
/// </summary>
public static class TemplateStore
{
    /// <summary>
    /// Materialises template files for <paramref name="defs"/> under <paramref name="templatesDir"/>.
    /// For each definition with a non-empty <see cref="TemplateDefinition.SourcePath"/> pointing at an
    /// existing file, the file is copied in under a unique name, <see cref="TemplateDefinition.TemplateFileName"/>
    /// is updated (the previous copy, if any, is deleted), and <c>SourcePath</c> is cleared. Files in
    /// <paramref name="templatesDir"/> not referenced by any definition are deleted.
    /// </summary>
    public static void SaveTemplates(string templatesDir, IList<TemplateDefinition> defs)
    {
        Directory.CreateDirectory(templatesDir);

        foreach (var def in defs)
        {
            if (string.IsNullOrWhiteSpace(def.SourcePath) || !File.Exists(def.SourcePath))
                continue;

            var stored = Guid.NewGuid().ToString("N") + Path.GetExtension(def.SourcePath);
            File.Copy(def.SourcePath, Path.Combine(templatesDir, stored), overwrite: true);

            if (!string.IsNullOrEmpty(def.TemplateFileName))
                TryDelete(Path.Combine(templatesDir, def.TemplateFileName));

            def.TemplateFileName = stored;
            def.SourcePath       = string.Empty;
        }

        PruneOrphans(templatesDir, defs);
    }

    private static void PruneOrphans(string templatesDir, IEnumerable<TemplateDefinition> defs)
    {
        var referenced = defs
            .Select(d => d.TemplateFileName)
            .Where(n => !string.IsNullOrEmpty(n))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var path in Directory.EnumerateFiles(templatesDir))
            if (!referenced.Contains(Path.GetFileName(path)))
                TryDelete(path);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
