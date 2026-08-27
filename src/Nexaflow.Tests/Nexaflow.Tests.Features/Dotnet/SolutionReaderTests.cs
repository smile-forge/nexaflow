using System.IO;
using Nexaflow.Features.Dotnet.Services;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Dotnet;

/// <summary>
/// Covers the parsing that makes Run work at all: <c>dotnet run</c> takes a project, never a solution, so a
/// selected solution has to be resolved to a runnable project first.
/// </summary>
[TestClass]
[CoversNode("dotnet-startup-picker")]
public class SolutionReaderTests
{
    private readonly List<string> _temp = [];

    private string _dir = string.Empty;

    [TestInitialize]
    public void Init()
    {
        _dir = Path.Combine(Path.GetTempPath(), "nexasln_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _temp.Add(_dir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        foreach (var dir in _temp)
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { }
    }

    /// <summary>Writes a file (creating any intermediate folders) and returns its absolute path.</summary>
    private string Write(string relativePath, string content)
    {
        var path = Path.Combine(_dir, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private const string Library = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup/></Project>";
    private const string ConsoleApp = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><OutputType>Exe</OutputType></PropertyGroup></Project>";
    private const string GuiApp = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><OutputType>WinExe</OutputType></PropertyGroup></Project>";
    private const string WebApp = "<Project Sdk=\"Microsoft.NET.Sdk.Web\"><PropertyGroup/></Project>";
    private const string ExplicitLibrary = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><OutputType>Library</OutputType></PropertyGroup></Project>";

    // A test project builds an Exe in this repo (EnableMSTestRunner) — the package reference is what
    // distinguishes it.
    private const string TestApp = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup><OutputType>Exe</OutputType></PropertyGroup>
          <ItemGroup><PackageReference Include="MSTest" Version="4.3.0" /></ItemGroup>
        </Project>
        """;

    // ── ReadProjects ──────────────────────────────────────────────────────────

    [TestMethod]
    public void ReadProjects_Slnx_FindsProjectsNestedInFolders()
    {
        Write("src/App/App.csproj", GuiApp);
        Write("src/Lib/Lib.csproj", Library);
        var sln = Write("My.slnx", """
            <Solution>
              <Configurations>
                <Platform Name="x64" />
              </Configurations>
              <Folder Name="/src/">
                <Project Path="src/Lib/Lib.csproj">
                  <Platform Project="x64" />
                </Project>
              </Folder>
              <Project Path="src/App/App.csproj" />
            </Solution>
            """);

        var projects = SolutionReader.ReadProjects(sln).Select(Path.GetFileName).ToList();

        // <Platform Project="x64"/> has a *Project attribute*, not a Project element name — it must not
        // be mistaken for a project entry.
        CollectionAssert.AreEquivalent(new[] { "App.csproj", "Lib.csproj" }, projects);
    }

    [TestMethod]
    public void ReadProjects_Slnx_ResolvesPathsRelativeToTheSolution()
    {
        var expected = Write("src/App/App.csproj", GuiApp);
        var sln = Write("My.slnx", """<Solution><Project Path="src/App/App.csproj" /></Solution>""");

        CollectionAssert.AreEqual(new[] { expected }, SolutionReader.ReadProjects(sln).ToList());
    }

    [TestMethod]
    public void ReadProjects_Sln_SkipsSolutionFolders()
    {
        Write("src/App/App.csproj", GuiApp);
        var sln = Write("My.sln", """
            Microsoft Visual Studio Solution File, Format Version 12.00
            Project("{2150E333-8FDC-42A3-9474-1A3956D46DE8}") = "src", "src", "{AAAAAAAA-0000-0000-0000-000000000001}"
            EndProject
            Project("{9A19103F-16F7-4668-BE54-9A1E7A4F7556}") = "App", "src\App\App.csproj", "{BBBBBBBB-0000-0000-0000-000000000002}"
            EndProject
            Global
            EndGlobal
            """);

        // The solution folder entry has the same line shape but names a folder, not a project file.
        var projects = SolutionReader.ReadProjects(sln).Select(Path.GetFileName).ToList();

        CollectionAssert.AreEqual(new[] { "App.csproj" }, projects);
    }

    [TestMethod]
    public void ReadProjects_MissingSolution_ReturnsEmpty()
        => Assert.AreEqual(0, SolutionReader.ReadProjects(Path.Combine(_dir, "nope.slnx")).Count);

    // ── IsRunnable ────────────────────────────────────────────────────────────

    [TestMethod]
    public void IsRunnable_ConsoleExe_True()
        => Assert.IsTrue(SolutionReader.IsRunnable(Write("A/A.csproj", ConsoleApp)));

    [TestMethod]
    public void IsRunnable_WinExe_True()
        => Assert.IsTrue(SolutionReader.IsRunnable(Write("A/A.csproj", GuiApp)));

    [TestMethod]
    public void IsRunnable_WebSdkWithoutOutputType_True()
        => Assert.IsTrue(SolutionReader.IsRunnable(Write("A/A.csproj", WebApp)));

    [TestMethod]
    public void IsRunnable_LibraryByDefault_False()
        => Assert.IsFalse(SolutionReader.IsRunnable(Write("A/A.csproj", Library)));

    [TestMethod]
    public void IsRunnable_ExplicitLibrary_False()
        => Assert.IsFalse(SolutionReader.IsRunnable(Write("A/A.csproj", ExplicitLibrary)));

    [TestMethod]
    public void IsRunnable_TestProjectBuildingAnExe_False()
        => Assert.IsFalse(SolutionReader.IsRunnable(Write("A/A.csproj", TestApp)),
                          "a test project builds an Exe but `dotnet test` is its verb, not `dotnet run`");

    [TestMethod]
    public void IsRunnable_UnparseableProject_False()
        => Assert.IsFalse(SolutionReader.IsRunnable(Write("A/A.csproj", "not xml at all")));

    // ── RunnableProjects ──────────────────────────────────────────────────────

    [TestMethod]
    public void RunnableProjects_ExcludesLibrariesAndTests()
    {
        Write("App/App.csproj", GuiApp);
        Write("Lib/Lib.csproj", Library);
        Write("Tests/Tests.csproj", TestApp);
        var sln = Write("My.slnx", """
            <Solution>
              <Project Path="Lib/Lib.csproj" />
              <Project Path="Tests/Tests.csproj" />
              <Project Path="App/App.csproj" />
            </Solution>
            """);

        var runnable = SolutionReader.RunnableProjects(sln);

        Assert.AreEqual(1, runnable.Count);
        Assert.AreEqual("App.csproj", runnable[0].DisplayName);
        Assert.IsFalse(runnable[0].IsSolution);
    }

    [TestMethod]
    public void RunnableProjects_OrdersGuiFirstThenAlphabetically()
    {
        Write("Zeta/Zeta.csproj", GuiApp);
        Write("Alpha/Alpha.csproj", GuiApp);
        Write("Cli/Cli.csproj", ConsoleApp);
        var sln = Write("My.slnx", """
            <Solution>
              <Project Path="Cli/Cli.csproj" />
              <Project Path="Zeta/Zeta.csproj" />
              <Project Path="Alpha/Alpha.csproj" />
            </Solution>
            """);

        var names = SolutionReader.RunnableProjects(sln).Select(p => p.DisplayName).ToList();

        // Declaration order says nothing about which project is the "startup" one — a solution records no
        // such thing — so GUI apps lead, then alphabetical.
        CollectionAssert.AreEqual(new[] { "Alpha.csproj", "Zeta.csproj", "Cli.csproj" }, names);
    }

    [TestMethod]
    public void RunnableProjects_NothingRunnable_ReturnsEmpty()
    {
        Write("Lib/Lib.csproj", Library);
        var sln = Write("My.slnx", """<Solution><Project Path="Lib/Lib.csproj" /></Solution>""");

        Assert.AreEqual(0, SolutionReader.RunnableProjects(sln).Count);
    }
}
