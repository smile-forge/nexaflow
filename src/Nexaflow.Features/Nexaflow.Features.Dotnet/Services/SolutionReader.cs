using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Nexaflow.Features.Dotnet.Models;

namespace Nexaflow.Features.Dotnet.Services;

/// <summary>
/// Reads the projects out of a solution and works out which of them <c>dotnet run</c> can actually
/// launch. Needed because <c>dotnet run</c> takes a project, never a solution — with a solution
/// selected the viewlet has to name a startup project itself.
/// </summary>
public static partial class SolutionReader
{
    private static readonly string[] ProjectExtensions = [".csproj", ".fsproj", ".vbproj"];

    /// <summary>SDKs that default <c>OutputType</c> to <c>Exe</c> without declaring it.</summary>
    private static readonly string[] ExecutableSdks = ["Microsoft.NET.Sdk.Web", "Microsoft.NET.Sdk.Worker"];

    private static readonly string[] TestPackagePrefixes = ["Microsoft.NET.Test.Sdk", "MSTest", "xunit", "NUnit"];

    // Project("{GUID}") = "Name", "relative\path.csproj", "{GUID}"
    // The capture needs no closing quote — [^"]+ already stops at one.
    [GeneratedRegex("""^Project\("\{[^}]*\}"\)\s*=\s*"[^"]*",\s*"([^"]+)""", RegexOptions.Multiline)]
    private static partial Regex SlnProjectLine();

    /// <summary>Absolute paths of every project file referenced by <paramref name="solutionPath"/>.
    /// Best-effort — an unreadable or malformed solution yields an empty list.</summary>
    public static IReadOnlyList<string> ReadProjects(string solutionPath)
    {
        try
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(solutionPath))!;
            var relative = Path.GetExtension(solutionPath).Equals(".slnx", StringComparison.OrdinalIgnoreCase)
                ? ReadSlnx(solutionPath)
                : ReadSln(solutionPath);

            return relative
                .Where(p => ProjectExtensions.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase))
                .Select(p => Path.GetFullPath(Path.Combine(dir, p.Replace('/', Path.DirectorySeparatorChar)
                                                            .Replace('\\', Path.DirectorySeparatorChar))))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    /// <summary>The projects of <paramref name="solutionPath"/> that <c>dotnet run</c> can launch, best
    /// guess first. A solution records no startup project (VS keeps that in the binary <c>.suo</c>), so
    /// declaration order says nothing — order GUI apps ahead of console apps, then alphabetically, and let
    /// the caller remember whatever the user actually picks.</summary>
    public static IReadOnlyList<DotnetTarget> RunnableProjects(string solutionPath)
        => ReadProjects(solutionPath)
            .Select(p => (Path: p, Kind: Classify(p)))
            .Where(x => x.Kind is ProjectKind.GuiExecutable or ProjectKind.ConsoleExecutable)
            .OrderBy(x => x.Kind == ProjectKind.GuiExecutable ? 0 : 1)
            .ThenBy(x => Path.GetFileName(x.Path), StringComparer.OrdinalIgnoreCase)
            .Select(x => new DotnetTarget(Path.GetFileName(x.Path), x.Path, IsSolution: false))
            .ToList();

    /// <summary>True when <c>dotnet run</c> can launch <paramref name="projectPath"/>.</summary>
    public static bool IsRunnable(string projectPath)
        => Classify(projectPath) is ProjectKind.GuiExecutable or ProjectKind.ConsoleExecutable;

    private enum ProjectKind { Library, ConsoleExecutable, GuiExecutable }

    /// <summary>
    /// Classifies a project from its XML alone — no MSBuild evaluation, so a property set in a
    /// <c>Directory.Build.props</c> is invisible here. That's the accepted trade-off: the check has to be
    /// cheap enough to run on every folder visit, and a wrong guess only costs the user one menu pick.
    /// </summary>
    private static ProjectKind Classify(string projectPath)
    {
        try
        {
            var root = XDocument.Load(projectPath).Root;
            if (root is null) return ProjectKind.Library;

            // A test project builds an Exe (this repo's do — EnableMSTestRunner) but running it isn't
            // "running the app"; `dotnet test` is the verb for those.
            if (IsTestProject(root)) return ProjectKind.Library;

            var outputType = root.Descendants("OutputType").FirstOrDefault()?.Value.Trim();
            if (string.Equals(outputType, "WinExe", StringComparison.OrdinalIgnoreCase))
                return ProjectKind.GuiExecutable;
            if (string.Equals(outputType, "Exe", StringComparison.OrdinalIgnoreCase))
                return ProjectKind.ConsoleExecutable;
            if (outputType is not null)
                return ProjectKind.Library;   // explicitly a Library

            var sdk = root.Attribute("Sdk")?.Value;
            return sdk is not null && ExecutableSdks.Contains(sdk, StringComparer.OrdinalIgnoreCase)
                ? ProjectKind.ConsoleExecutable
                : ProjectKind.Library;
        }
        catch
        {
            return ProjectKind.Library;   // unreadable → never offer it as a run target
        }
    }

    private static bool IsTestProject(XElement root)
    {
        if (root.Descendants("IsTestProject")
                .Any(e => string.Equals(e.Value.Trim(), "true", StringComparison.OrdinalIgnoreCase)))
            return true;

        return root.Descendants("PackageReference")
                   .Select(e => e.Attribute("Include")?.Value)
                   .Any(include => include is not null
                        && TestPackagePrefixes.Any(p => include.StartsWith(p, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>Every <c>&lt;Project Path="…"/&gt;</c> in the XML, including those nested in
    /// <c>&lt;Folder&gt;</c>s. The <c>&lt;Platform Project="x64"/&gt;</c> children carry a <em>Project
    /// attribute</em>, not a Project element name, so matching on element name is safe.</summary>
    private static IEnumerable<string> ReadSlnx(string solutionPath)
        => XDocument.Load(solutionPath)
                    .Descendants("Project")
                    .Select(e => e.Attribute("Path")?.Value)
                    .Where(p => !string.IsNullOrWhiteSpace(p))!;

    /// <summary>Project entries of a classic <c>.sln</c>. Solution folders share the same line shape but
    /// name a folder rather than a project file — the extension filter in <see cref="ReadProjects"/>
    /// drops them.</summary>
    private static IEnumerable<string> ReadSln(string solutionPath)
        => SlnProjectLine().Matches(File.ReadAllText(solutionPath))
                           .Select(m => m.Groups[1].Value);
}
