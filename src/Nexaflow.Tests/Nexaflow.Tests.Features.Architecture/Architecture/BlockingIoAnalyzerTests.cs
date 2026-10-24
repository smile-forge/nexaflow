using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

using Nexaflow.Analyzers.Io;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Architecture;

/// <summary>
/// What <see cref="BlockingIoAnalyzer"/> holds a feature to: the file system awaited, never blocked on, and never stepped
/// around through <c>System.IO</c> — unless the code says, with a reason, that its files are on this machine's own disk.
/// Each put to it in a compilation of its own, against a file system of the same shape as the real one.
/// </summary>
[TestClass]
[NoCoverage("whole-repo architecture guard; maps to no single product node")]
public class BlockingIoAnalyzerTests
{
    /// <summary>The file system and its two marks, as the real ones are shaped.</summary>
    private const string FileSystem = """
        namespace Nexaflow.IO.Common
        {
            using System.Threading.Tasks;

            [System.AttributeUsage(System.AttributeTargets.Method)]
            public sealed class BlocksOnIoAttribute(string? alternative) : System.Attribute { public string? Alternative => alternative; }

            public sealed class LocalDiskOnlyAttribute(string reason) : System.Attribute { public string Reason => reason; }

            public interface IVirtualFileSystem
            {
                [BlocksOnIo(nameof(ReadAllTextAsync))] string ReadAllText(string path);
                [BlocksOnIo(null)] System.IO.Stream OpenReadWrite(string path);
                ValueTask<string> ReadAllTextAsync(string path);
                string GetDisplayName(string path);
            }

            public sealed class VirtualFileSystem : IVirtualFileSystem
            {
                public static VirtualFileSystem Instance { get; } = new();
                public string ReadAllText(string path) => "";
                public System.IO.Stream OpenReadWrite(string path) => System.IO.Stream.Null;
                public ValueTask<string> ReadAllTextAsync(string path) => new("");
                public string GetDisplayName(string path) => path;
            }
        }
        """;

    private const string Usings = "#nullable enable\nusing System; using System.IO; using System.Threading.Tasks;\n";

    private static IReadOnlyList<Diagnostic> Analysed(bool withFileSystem, params string[] sources)
    {
        var usings = withFileSystem ? Usings + "using Nexaflow.IO.Common;\n" : Usings;
        var trees = (withFileSystem ? new[] { "#nullable enable\n" + FileSystem } : [])
            .Concat(sources.Select(source => usings + source))
            .Select(source => CSharpSyntaxTree.ParseText(source));
        var runtime = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(path => string.Equals(Path.GetDirectoryName(path), runtime, StringComparison.OrdinalIgnoreCase))
            .Select(path => MetadataReference.CreateFromFile(path));

        var compilation = CSharpCompilation.Create("Feature", trees, references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.AreEqual(0, compilation.GetDiagnostics().Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error),
                        string.Join("\n", compilation.GetDiagnostics()));

        return [.. compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new BlockingIoAnalyzer()))
                              .GetAnalyzerDiagnosticsAsync().Result
                              .OrderBy(diagnostic => diagnostic.Location.SourceSpan.Start)];
    }

    private static List<string> Found(params string[] sources) =>
        [.. Analysed(withFileSystem: true, sources).Select(diagnostic => diagnostic.Id)];

    [TestMethod]
    [TestCategory("Unit")]
    public void AwaitingTheFileSystemIsNothingToSay() =>
        CollectionAssert.AreEqual(Array.Empty<string>(), Found("""
            sealed class Viewer(IVirtualFileSystem vfs)
            {
                public async Task<string> Load(string path) => vfs.GetDisplayName(path) + await vfs.ReadAllTextAsync(path);
            }
            """));

    [TestMethod]
    [TestCategory("Unit")]
    public void ABlockingMemberIsSaidToBe_ThroughTheContractOrTheTypeBehindIt_AndTheReportNamesWhatToAwait()
    {
        var said = Analysed(withFileSystem: true, """
            sealed class Viewer(IVirtualFileSystem vfs)
            {
                public string Load(string path) => vfs.ReadAllText(path) + VirtualFileSystem.Instance.ReadAllText(path);
            }
            """);

        CollectionAssert.AreEqual(new[] { "NXIO001", "NXIO001" }, said.Select(diagnostic => diagnostic.Id).ToList());
        StringAssert.Contains(said[0].GetMessage(), "IVirtualFileSystem.ReadAllText");
        StringAssert.Contains(said[0].GetMessage(), "await ReadAllTextAsync");
        StringAssert.Contains(said[1].GetMessage(), "VirtualFileSystem.ReadAllText");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void AMemberWithNothingToAwaitIsSaidToReachLocalPathsOnly()
    {
        var said = Analysed(withFileSystem: true, """
            sealed class Hex(IVirtualFileSystem vfs) { public Stream Open(string path) => vfs.OpenReadWrite(path); }
            """).Single();

        Assert.AreEqual("NXIO001", said.Id);
        StringAssert.Contains(said.GetMessage(), "local paths only");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void SystemIoReachingTheDiskIsSaidToBe() =>
        CollectionAssert.AreEqual(new[] { "NXIO002", "NXIO002", "NXIO002", "NXIO002", "NXIO002", "NXIO002", "NXIO002", "NXIO002" }, Found("""
            sealed class Viewer
            {
                public void Load(string path)
                {
                    File.ReadAllText(path);
                    Directory.Exists(path);
                    _ = new FileInfo(path);
                    _ = new DirectoryInfo(path);
                    using var stream = new FileStream(path, FileMode.Open);
                    using var watcher = new FileSystemWatcher(path);
                    using var reader = new StreamReader(path);
                    _ = Path.Exists(path);
                }
            }
            """));

    [TestMethod]
    [TestCategory("Unit")]
    public void SystemIoThatNeverTouchesTheDiskIsNothingToSay() =>
        CollectionAssert.AreEqual(Array.Empty<string>(), Found("""
            sealed class Viewer
            {
                public string Load(Stream stream, string folder)
                {
                    using var reader = new StreamReader(stream);
                    using var writer = new StreamWriter(new MemoryStream());
                    return Path.Combine(folder, Path.GetFileName("a.txt")) + reader.ReadToEnd();
                }
            }
            """));

    [TestMethod]
    [TestCategory("Unit")]
    public void CodeMarkedLocalDiskOnlyMayReachTheDisk_InsideLambdasAndAccessorsToo() =>
        CollectionAssert.AreEqual(Array.Empty<string>(), Found("""
            [LocalDiskOnly("the feature's settings under %APPDATA%")]
            sealed class SettingsStore(IVirtualFileSystem vfs)
            {
                public string Load(string path) => File.ReadAllText(path) + vfs.ReadAllText(path);
            }

            sealed class Viewer
            {
                [LocalDiskOnly("thumbnails cached under %LOCALAPPDATA%")]
                public Func<string, bool> Cached() => path => File.Exists(path);

                [LocalDiskOnly("the recent list under %APPDATA%")]
                public string Recent => File.ReadAllText("recent.json");
            }
            """));

    [TestMethod]
    [TestCategory("Unit")]
    public void TheMarkCoversOnlyWhatItHolds() =>
        CollectionAssert.AreEqual(new[] { "NXIO002" }, Found("""
            sealed class Viewer
            {
                [LocalDiskOnly("the feature's settings under %APPDATA%")]
                public string Settings() => File.ReadAllText("settings.json");

                public string Open(string path) => File.ReadAllText(path);
            }
            """));

    [TestMethod]
    [TestCategory("Unit")]
    public void AMarkThatGivesNoReasonIsSaidToBe() =>
        CollectionAssert.AreEqual(new[] { "NXIO003" }, Found("""
            [LocalDiskOnly(" ")]
            sealed class Store { public string Load() => File.ReadAllText("x"); }
            """));

    [TestMethod]
    [TestCategory("Unit")]
    public void WhereThereIsNoFileSystemSystemIoIsStillHeld() =>
        CollectionAssert.AreEqual(new[] { "NXIO002" }, Analysed(withFileSystem: false, """
            sealed class Tool { public string Load(string path) => File.ReadAllText(path); }
            """).Select(diagnostic => diagnostic.Id).ToList());
}
