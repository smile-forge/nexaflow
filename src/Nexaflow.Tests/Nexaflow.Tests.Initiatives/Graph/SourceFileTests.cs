using System;
using System.IO;
using Nexaflow.Services.Initiatives.Graph;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Initiatives.Graph;

/// <summary>
/// The bytes around an edit rather than the edit itself: the encoding a file is written back with, and the
/// line ending a file that does not exist yet should be born with.
/// <para>
/// The second of those is the only place in the whole editing path where there is no file to copy the shape
/// from, and it was the only place that answered with <see cref="Environment.NewLine"/> — a fact about the
/// machine. On Windows that put CRLF into an LF repository, which is the whole-file diff every other part of
/// this design exists to avoid.
/// </para>
/// </summary>
[TestClass]
[CoversNode("graph-edit")]
public class SourceFileTests
{
    private string _root = "";

    [TestInitialize]
    public void Setup() => _root = Directory.CreateTempSubdirectory("nexa-sourcefile-").FullName;

    [TestCleanup]
    public void Cleanup() { try { Directory.Delete(_root, recursive: true); } catch { } }

    private string Write(string relative, string content)
    {
        var path = Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private static string Lines(string ending, params string[] lines) =>
        string.Join(ending, lines) + ending;

    [DataTestMethod]
    [DataRow("\n")]
    [DataRow("\r\n")]
    public void NewFile_TakesTheEndingItsNeighboursUse(string ending)
    {
        Write("src/One.cs",   Lines(ending, "class One", "{", "}"));
        Write("src/Two.cs",   Lines(ending, "class Two", "{", "}"));
        Write("src/Three.cs", Lines(ending, "class Three", "{", "}"));

        Assert.AreEqual(ending, SourceFile.NewlineFor(Path.Combine(_root, "src", "New.cs"), _root));
    }

    /// <summary>A new directory is the normal case for a new file — the convention it must join is its
    /// parent's, and asking only the empty directory it lands in would find nothing and fall back to the
    /// machine's ending.</summary>
    [TestMethod]
    public void NewFile_InANewDirectory_TakesTheConventionFromAbove()
    {
        Write("src/One.cs", Lines("\n", "class One", "{", "}"));
        Write("src/Two.cs", Lines("\n", "class Two", "{", "}"));

        Assert.AreEqual("\n", SourceFile.NewlineFor(Path.Combine(_root, "src", "Feature", "Deep", "New.cs"), _root));
    }

    /// <summary>Only files of the same kind are asked, so a directory of CRLF text beside LF source cannot
    /// answer for the source — and nothing has to sniff whether a neighbour is text at all.</summary>
    [TestMethod]
    public void NewFile_IgnoresNeighboursOfAnotherKind()
    {
        Write("src/One.cs",  Lines("\n", "class One", "{", "}"));
        Write("src/a.txt",   Lines("\r\n", "a", "b", "c"));
        Write("src/b.txt",   Lines("\r\n", "a", "b", "c"));
        Write("src/c.txt",   Lines("\r\n", "a", "b", "c"));

        Assert.AreEqual("\n", SourceFile.NewlineFor(Path.Combine(_root, "src", "New.cs"), _root));
    }

    /// <summary>A single-line neighbour has no ending to report. Counting it would let the file with the
    /// least to say about the convention decide it, because a file with no newline in it reads as LF.</summary>
    [TestMethod]
    public void NewFile_IgnoresANeighbourWithNoLineEndingAtAll()
    {
        Write("src/Marker.cs", "class Marker { }");
        Write("src/One.cs",    Lines("\r\n", "class One", "{", "}"));

        Assert.AreEqual("\r\n", SourceFile.NewlineFor(Path.Combine(_root, "src", "New.cs"), _root));
    }

    /// <summary>With nothing nearby to copy, the machine's ending is the honest answer — there is no
    /// convention to preserve.</summary>
    [TestMethod]
    public void NewFile_WithNoNeighbours_FallsBackToThePlatform()
    {
        Assert.AreEqual(Environment.NewLine,
                        SourceFile.NewlineFor(Path.Combine(_root, "empty", "New.cs"), _root));
    }

    /// <summary>An existing file is written back byte-identical apart from the edit — the guard the whole
    /// type exists for, restated for the ending rather than the byte-order mark.</summary>
    [DataTestMethod]
    [DataRow("\n")]
    [DataRow("\r\n")]
    public void ReadAndWrite_RoundTripsAFilesEndingsUntouched(string ending)
    {
        var content = Lines(ending, "class One", "{", "}");
        var path    = Write("src/One.cs", content);

        var read = SourceFile.Read(path);
        Assert.IsNotNull(read);
        Assert.AreEqual(content, read!.Value.Text);

        Assert.IsNull(SourceFile.WriteIfUnchanged(path, content, read.Value.Text, read.Value.Encoding));
        Assert.AreEqual(content, File.ReadAllText(path));
    }
}
