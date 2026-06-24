using System;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Nexaflow.Tests.Fixtures;

/// <summary>
/// Archive fixtures for the Compressed feature, covering the formats that can be produced with no extra
/// dependency: zip, nested zip (zip-in-zip), tar, tar.gz and a single-stream gzip. Built with fixed
/// entry timestamps (and the dependency-free <c>ustar</c> tar format) so the bytes are stable across
/// runs. The remaining formats (7z/rar/zst/lz4/AES) are exercised by the provider unit tests, which
/// generate their own in-memory fixtures.
/// </summary>
public sealed class ArchiveSamples : ISampleSet
{
    // Fixed epoch so the produced archives are byte-identical every run.
    private static readonly DateTimeOffset Epoch = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public string SubDirectory => "archive";

    public IReadOnlyList<SampleFile> Files { get; } = Build();

    private static IReadOnlyList<SampleFile> Build()
    {
        var inner = Zip(("hello.txt", Utf8("hello from inside a nested archive")));
        var tar = Tar(
            ("readme.txt", Utf8("# Tar sample\nA file inside a tar.\n")),
            ("docs/notes.txt", Utf8("notes inside docs\n")));

        return
        [
            SampleFile.Raw("sample.zip", Zip(
                ("readme.txt", Utf8("# Archive sample\nText file inside a zip.\n")),
                ("docs/data.json", Utf8("{ \"name\": \"sample\", \"values\": [1, 2, 3] }\n")))),

            SampleFile.Raw("nested.zip", Zip(
                ("top.txt", Utf8("top-level entry")),
                ("inner.zip", inner))),

            SampleFile.Raw("bundle.tar", tar),
            SampleFile.Raw("bundle.tar.gz", Gzip(tar)),
            SampleFile.Raw("readme.txt.gz", Gzip(Utf8("# Gzipped single file\n"))),
        ];
    }

    private static byte[] Utf8(string s) => Encoding.UTF8.GetBytes(s);

    private static byte[] Tar(params (string Name, byte[] Content)[] entries)
    {
        using var ms = new MemoryStream();
        using (var writer = new TarWriter(ms, TarEntryFormat.Ustar, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                var entry = new UstarTarEntry(TarEntryType.RegularFile, name)
                {
                    ModificationTime = Epoch,
                    DataStream = new MemoryStream(content),
                };
                writer.WriteEntry(entry);
            }
        }
        return ms.ToArray();
    }

    private static byte[] Gzip(byte[] data)
    {
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true))
            gz.Write(data, 0, data.Length);
        return ms.ToArray();
    }

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
