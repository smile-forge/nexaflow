using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace Nexaflow.Services.Initiatives.Cli.Building;

/// <summary>
/// The state of everything a restore of one project reads, found without evaluating anything — so that a build whose
/// restore inputs are as they were at its last successful restore can leave the restore out. A no-op restore is not
/// free: NuGet evaluates every project in the closure to learn it has nothing to do, a third of a no-op build of a
/// large suite.
/// <para>
/// Read off the project files as written, the way the graph reads them: each project's <c>ProjectReference</c>s and the
/// files it imports, every <c>Directory.Build.*</c>, <c>Directory.Packages.props</c>, <c>NuGet.config</c> and
/// <c>global.json</c> above it, and whether its <c>project.assets.json</c> is there at all. Anything that cannot be
/// named exactly — a solution, a path built from a property, a file that will not parse — makes the stamp null, and a
/// null always restores.
/// </para>
/// </summary>
internal static class RestoreInputs
{
    private static readonly string[] ProjectExtensions = [".csproj", ".vbproj", ".fsproj"];
    private static readonly string[] Above = ["Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props",
                                              "NuGet.config", "global.json"];

    /// <summary>The stamp of what restoring <paramref name="entry"/> reads, or null when that cannot all be named.</summary>
    public static string? StampOf(string entry)
    {
        if (!ProjectExtensions.Contains(Path.GetExtension(entry), StringComparer.OrdinalIgnoreCase)) return null;

        var files    = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var projects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending  = new Stack<string>([Path.GetFullPath(entry)]);
        while (pending.Count > 0)
        {
            var project = pending.Pop();
            if (!projects.Add(project)) continue;
            if (!Read(project, files, pending)) return null;

            for (var dir = Path.GetDirectoryName(project); dir is not null; dir = Path.GetDirectoryName(dir))
                foreach (var name in Above)
                {
                    var candidate = Path.Combine(dir, name);
                    if (name.StartsWith("Directory.Build.", StringComparison.Ordinal) && File.Exists(candidate))
                    {
                        if (!Read(candidate, files, references: null)) return null;
                    }
                    else files.Add(candidate);
                }
            files.Add(Path.Combine(Path.GetDirectoryName(project)!, "obj", "project.assets.json"));
        }

        // The user's own NuGet configuration is read by every restore.
        files.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NuGet", "NuGet.Config"));

        var text = new StringBuilder();
        foreach (var file in files)
        {
            var info = new FileInfo(file);
            text.Append(file).Append('|').Append(info.Exists ? info.LastWriteTimeUtc.Ticks : -1).Append('|')
                .Append(info.Exists ? info.Length : -1).Append('\n');
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    /// <summary>
    /// Adds <paramref name="file"/> and everything it imports, and queues the projects it references when
    /// <paramref name="references"/> is given. False when a path cannot be read off the page.
    /// </summary>
    private static bool Read(string file, SortedSet<string> files, Stack<string>? references)
    {
        if (!files.Add(file) || !File.Exists(file)) return true;

        XDocument document;
        try { document = XDocument.Load(file); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Xml.XmlException) { return false; }

        var directory = Path.GetDirectoryName(file)! + Path.DirectorySeparatorChar;
        foreach (var element in document.Descendants())
        {
            var named = element.Name.LocalName switch
            {
                "Import"                                => element.Attribute("Project")?.Value,
                "ProjectReference" when references is not null => element.Attribute("Include")?.Value,
                _                                       => null,
            };
            if (named is null) continue;

            var path = named.Trim().Replace("$(MSBuildThisFileDirectory)", directory, StringComparison.OrdinalIgnoreCase);
            if (path.Length == 0 || path.IndexOfAny(['$', '@', '%', '*', '?', ';']) >= 0) return false;

            var full = Path.GetFullPath(Path.Combine(directory, path));
            if (element.Name.LocalName == "ProjectReference") references!.Push(full);
            else if (!Read(full, files, references: null)) return false;
        }
        return true;
    }
}
