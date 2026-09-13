using System.Diagnostics;
using Nexaflow.Syntax.Compiler;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Components.Compiler;

/// <summary>
/// The compiler asked what an edit did, over two small projects on disk — a library and an app that references it —
/// so the real design-time build hands out the real command lines. Nothing here writes a source file: every edit is a
/// before and an after held in memory, which is also what a dry run asks.
/// </summary>
[TestClass]
[CoversNode("syntax-compile-check")]
[DoNotParallelize]
public class CompileHostTests
{
    private static string _root = "";
    private static CompileHost _host = null!;
    private static readonly TimeSpan Budget = TimeSpan.FromMinutes(2);

    private const string Greeter =
        "namespace Lib;\n\npublic class Greeter\n{\n    public string Greet(string name) => \"hi \" + name;\n}\n";

    private const string Calc =
        "namespace Lib;\n\npublic class Calc\n{\n    public int Bad => Missing;\n}\n";

    private static string Lib(string file) => Path.Combine(_root, "Lib", file);
    private static string App(string file) => Path.Combine(_root, "App", file);

    [ClassInitialize]
    public static void Build(TestContext _)
    {
        _root = Directory.CreateTempSubdirectory("nexa-compile-").FullName;

        Write(Lib("Lib.csproj"),
              "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework>"
            + "<Nullable>enable</Nullable></PropertyGroup></Project>");
        Write(Lib("Greeter.cs"), Greeter);
        Write(Lib("Calc.cs"), Calc);

        Write(App("App.csproj"),
              "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework>"
            + "<Nullable>enable</Nullable></PropertyGroup><ItemGroup><ProjectReference Include=\"..\\Lib\\Lib.csproj\" />"
            + "</ItemGroup></Project>");
        Write(App("Program.cs"),
              "using Lib;\n\npublic static class Program\n{\n    public static string Run() => new Greeter().Greet(\"x\");\n}\n");
        Write(App("Other.cs"),
              "public class Other\n{\n    public string Greet() => \"\";\n    public string Use() => Greet();\n}\n");

        using var restore = Process.Start(new ProcessStartInfo("dotnet", ["restore", App("App.csproj"), "-nologo", "-v:q"])
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
        })!;
        restore.StandardOutput.ReadToEnd();
        restore.WaitForExit();
        Assert.AreEqual(0, restore.ExitCode, "the sample projects need restoring before anything can read them");

        _host = new CompileHost(_root);
    }

    [ClassCleanup]
    public static void Remove()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    [TestMethod]
    public void AnErrorTheEditIntroduces_IsReported_AndOneThatWasThereAlreadyIsNot()
    {
        var after  = Calc.Replace("}\n", "    public int Worse => \"text\";\n}\n");
        var report = _host.Check([new SourceChange(Lib("Calc.cs"), Calc, after)], [], Budget);

        Assert.AreEqual(0, report.NotChecked.Count, string.Join("\n", report.NotChecked));
        var introduced = report.Introduced.Single();
        Assert.AreEqual("CS0029", introduced.Id, introduced.Message);
        Assert.AreEqual(6, introduced.Line);
        CollectionAssert.Contains(report.Checked.ToList(), "Lib");
    }

    [TestMethod]
    public void AnErrorTheEditRemoves_IsReportedAsFixed()
    {
        var report = _host.Check([new SourceChange(Lib("Calc.cs"), Calc, Calc.Replace("Missing", "1"))], [], Budget);

        Assert.AreEqual(0, report.Introduced.Count);
        Assert.AreEqual("CS0103", report.Fixed.Single().Id);
    }

    [TestMethod]
    public void ChangingASignature_ReportsTheCallerItBreaks_InTheProjectThatReferencesIt()
    {
        var after  = Greeter.Replace("Greet(string name)", "Greet(string name, int times)");
        var report = _host.Check([new SourceChange(Lib("Greeter.cs"), Greeter, after)],
                                 [App("Program.cs"), App("Other.cs")], Budget);

        var broken = report.Introduced.Single();
        Assert.AreEqual(App("Program.cs"), broken.FullPath, true, "the call in the app no longer fits");
        Assert.AreEqual("CS7036", broken.Id, broken.Message);
        CollectionAssert.Contains(report.Checked.ToList(), "App");
    }

    [TestMethod]
    public void AFileNoProjectCompiles_IsNamedAsNotChecked()
    {
        var loose  = Path.Combine(_root, "loose.cs");
        var report = _host.Check([new SourceChange(loose, "class A {}", "class A { int x = \"y\"; }")], [], Budget);

        Assert.AreEqual(0, report.Introduced.Count);
        StringAssert.Contains(report.NotChecked.Single(), "no project");
    }

    [TestMethod]
    public void References_FollowTheBinding_NotTheSpelling()
    {
        var declaration = Greeter.IndexOf("Greet(", StringComparison.Ordinal);
        var report = _host.FindReferences(Lib("Greeter.cs"), declaration, [App("Program.cs"), App("Other.cs")], Budget);

        Assert.IsNull(report.Error, report.Error);
        CollectionAssert.AreEquivalent(
            new[] { Lib("Greeter.cs").ToUpperInvariant(), App("Program.cs").ToUpperInvariant() },
            report.Locations.Select(l => l.FullPath.ToUpperInvariant()).ToArray(),
            "the declaration and the call in the app - not Other.Greet, which only shares the name");
        Assert.AreEqual(declaration, report.Locations.Single(l => l.FullPath.EndsWith("Greeter.cs")).Start);
    }
}
