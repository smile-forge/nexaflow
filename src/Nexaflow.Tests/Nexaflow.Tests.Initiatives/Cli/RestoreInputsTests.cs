using Nexaflow.Services.Initiatives.Cli.Building;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Initiatives.Cli;

/// <summary>
/// What a restore reads, stamped without evaluating anything: the same until a file a restore reads changes, and absent —
/// so the restore runs — wherever the files cannot all be named.
/// </summary>
[TestClass]
[CoversNode("nfi-build")]
public class RestoreInputsTests
{
    private string _root = "";

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(TestTemp.Root, "restore-inputs-" + Guid.NewGuid().ToString("N")[..8]);
        Write("Directory.Build.props", "<Project />");
        Write("App/App.csproj", """<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><ProjectReference Include="..\Lib\Lib.csproj" /></ItemGroup></Project>""");
        Write("Lib/Lib.csproj", """<Project Sdk="Microsoft.NET.Sdk"><Import Project="..\shared.props" /></Project>""");
        Write("shared.props", "<Project />");
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [TestMethod]
    public void TheStamp_HoldsUntilSomethingARestoreReadsChanges()
    {
        var app   = Path.Combine(_root, "App", "App.csproj");
        var first = RestoreInputs.StampOf(app);

        Assert.IsNotNull(first);
        Assert.AreEqual(first, RestoreInputs.StampOf(app), "nothing changed");

        Write("shared.props", "<Project><PropertyGroup><X>1</X></PropertyGroup></Project>");
        var imported = RestoreInputs.StampOf(app);
        Assert.AreNotEqual(first, imported, "a file a referenced project imports");

        Write("Directory.Build.props", "<Project><PropertyGroup><Y>1</Y></PropertyGroup></Project>");
        var above = RestoreInputs.StampOf(app);
        Assert.AreNotEqual(imported, above, "a Directory.Build.props above it");

        Write("Lib/obj/project.assets.json", "{}");
        Assert.AreNotEqual(above, RestoreInputs.StampOf(app), "a restore output appearing, or going");
    }

    [TestMethod]
    public void WhatCannotBeNamed_HasNoStamp_SoItsRestoreRuns()
    {
        Write("Lib/Lib.csproj", """<Project Sdk="Microsoft.NET.Sdk"><Import Project="$(SomeRoot)\shared.props" /></Project>""");
        Assert.IsNull(RestoreInputs.StampOf(Path.Combine(_root, "App", "App.csproj")), "an import through a property");

        Write("All.slnx", "<Solution />");
        Assert.IsNull(RestoreInputs.StampOf(Path.Combine(_root, "All.slnx")), "a solution");
    }

    private void Write(string relative, string text)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(Interlocked.Increment(ref _written)));   // distinct, whatever the clock's grain
    }

    private static int _written;
}
