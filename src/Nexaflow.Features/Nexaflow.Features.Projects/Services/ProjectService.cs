using Nexaflow.Features.Projects.Model;
using System.IO;
using System.Text.Json;

namespace Nexaflow.Features.Projects.Services;

/// <summary>
/// Shell-side bootstrap that persists the project root path to
/// %APPDATA%\Aria\Shell\settings.json and vends the singleton
/// <see cref="ProjectOperations"/> instance.
///
/// View-models should hold a reference to <see cref="Ops"/> and call
/// <see cref="ProjectOperations"/> methods directly — no delegation needed.
/// </summary>
public static class ProjectService
{
    private static readonly string _settingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Aria", "Shell", "settings.json");

    private record ShellSettings(string ProjectRoot);

    private static string LoadProjectRoot()
    {
        if (File.Exists(_settingsPath))
        {
            try
            {
                var s = JsonSerializer.Deserialize<ShellSettings>(File.ReadAllText(_settingsPath));
                if (s is { ProjectRoot.Length: > 0 }) return s.ProjectRoot;
            }
            catch { /* ignore corrupt file */ }
        }
        return @"d:\Projects";
    }

    // ── Singleton ProjectOperations ────────────────────────────────────────
    private static ProjectOperations _ops = new(LoadProjectRoot());

    /// <summary>The live <see cref="ProjectOperations"/> instance. Use this directly.</summary>
    public static ProjectOperations Ops => _ops;

    /// <summary>
    /// Changes the root path, recreates <see cref="Ops"/>, and persists the setting.
    /// </summary>
    public static void SetRootPath(string path)
    {
        if (string.Equals(_ops.RootPath, path, StringComparison.OrdinalIgnoreCase)) return;
        _ops = new ProjectOperations(path);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
            File.WriteAllText(_settingsPath,
                JsonSerializer.Serialize(new ShellSettings(path)));
        }
        catch { /* best effort */ }
    }

    /// <summary>Convenience: full filesystem path for a project folder.</summary>
    public static string GetProjectPath(string folderName)
        => Path.Combine(_ops.RootPath, folderName);
}
