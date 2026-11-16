using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.IO.Documents.Processing;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Documents;

/// <summary>
/// Notes kept in files: the same note kept by many runs at once — a passage a document repeats, translated side by side —
/// is kept, every run carrying on, and nothing half-written is left beside it.
/// </summary>
[TestClass]
[CoversNode("agent-document-tools")]
public class DocumentNotesFolderTests
{
    private string _root = string.Empty;

    [TestInitialize]
    public void Setup() => _root = TestTemp.CreateSubdirectory("notes-").FullName;

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [TestMethod]
    public async Task OneNoteKeptByManyRunsAtOnce_IsKept_AndNoRunFails()
    {
        var notes = new DocumentNotesFolder(_root, Path.Combine(_root, "Report.md"));
        var texts = Enumerable.Range(0, 32).Select(run => new string((char)('a' + run % 26), 256 * 1024)).ToList();

        await Parallel.ForEachAsync(texts, new ParallelOptions { MaxDegreeOfParallelism = texts.Count },
                                    async (text, token) => await notes.WriteAsync("translation", "header", text, token));

        var kept = await notes.ReadAsync("translation", "header", CancellationToken.None);
        CollectionAssert.Contains(texts, kept, "one run's note, whole");
        Assert.IsFalse(Directory.EnumerateFiles(_root, "*.writing", SearchOption.AllDirectories).Any(), "nothing half-written left beside it");
    }
}
