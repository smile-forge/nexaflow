using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Nexaflow.Tests.Fixtures;

/// <summary>
/// Archive fixtures for the Compressed feature: a plain zip and a nested zip (zip-in-zip) for the
/// virtual-file-system / browse-as-folder paths. Built with fixed entry timestamps so the bytes are
/// stable across runs (no regeneration churn). Richer formats (tar/7z/zst/AES) are exercised by the
/// handler-level unit tests, which generate their own fixtures.
/// </summary>
public sealed class ArchiveSamples : ISampleSet
{
    // Fixed epoch so the produced archives are byte-identical every run.
    private static readonly DateTimeOffset Epoch = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public string SubDirectory => "archive";

    public IReadOnlyList<SampleFile> Files { get; } = Build();

    private static IReadOnlyList<SampleFile> Build()
    {
        var inner = Zip(
            ("hello.txt", Utf8("hello from inside a nested archive")));

        return
        [
            SampleFile.Raw("sample.zip", Zip(
                ("readme.txt", Utf8("# Archive sample\nText file inside a zip.\n")),
                ("docs/data.json", Utf8("{ \"name\": \"sample\", \"values\": [1, 2, 3] }\n")))),

            SampleFile.Raw("nested.zip", Zip(
                ("top.txt", Utf8("top-level entry")),
                ("inner.zip", inner))),
        ];
    }

    private static byte[] Utf8(string s) => Encoding.UTF8.GetBytes(s);

    private static byte[] Zip(params (string Name, byte[] Content)[] entries)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
                entry.LastWriteTime = Epoch;
                using var s = entry.Open();
                s.Write(content, 0, content.Length);
            }
        }
        return ms.ToArray();
    }
}
