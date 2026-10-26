using Nexaflow.IO.Common;
using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.Text;
using Nexaflow.Features.Common;
using Nexaflow.Features.Images.Details;
using Nexaflow.Features.Images.Editing;
using Nexaflow.Features.Images.Editing.Codecs;
using Nexaflow.Features.Images.Editing.Recipe;
using Nexaflow.Features.Images.ViewModels;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Common.Localization;
using NSubstitute;
using SkiaSharp;
using static Nexaflow.Tests.Features.Images.EditingFixtures;

namespace Nexaflow.Tests.Features.Images;

/// <summary>
/// The Details panel: what a picture's EXIF block, GPS directory and colour profile say, read in either byte order and
/// put into words, and the panel reading the picture on screen only while it is open.
/// </summary>
[TestClass]
[CoversNode("images-exif")]
public class PhotoDetailsTests
{
    private CultureInfo _culture = null!;

    [TestInitialize]
    public void FixTheCulture()
    {
        _culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
    }

    [TestCleanup]
    public void PutTheCultureBack() => CultureInfo.CurrentCulture = _culture;

    // ── The camera's block ──────────────────────────────────────────────────

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ACamerasBlock_ReadsAsTheShotItDescribes_InEitherByteOrder(bool bigEndian)
    {
        var facts = ExifFacts.Read(Shot(bigEndian));

        Assert.AreEqual("Canon", facts.Make);
        Assert.AreEqual("Canon EOS R6", facts.Model);
        Assert.AreEqual("RF24-105mm F4 L IS USM", facts.Lens);
        Assert.AreEqual(2.8, facts.FNumber!.Value, 1e-9);
        Assert.AreEqual(1.0 / 250, facts.ExposureSeconds!.Value, 1e-12);
        Assert.AreEqual(400, facts.Iso);
        Assert.AreEqual(50, facts.FocalLength!.Value, 1e-9);
        Assert.AreEqual(80, facts.FocalLength35mm);
        Assert.AreEqual(true, facts.FlashFired, "bit 0 of 0x19 says the flash fired");
        Assert.AreEqual(-0.7, facts.ExposureBias!.Value, 1e-9, "a signed rational keeps its sign");
        Assert.AreEqual(new DateTime(2024, 3, 15, 14, 3, 22), facts.Taken);
        Assert.AreEqual(TimeSpan.FromHours(1), facts.TakenOffset);
        Assert.AreEqual("Ada Lovelace", facts.Artist);
        Assert.AreEqual("© 2024 Ada Lovelace", facts.Copyright, "UTF-8 written into an ASCII field is read as written");
        Assert.IsTrue(facts.DescribesTheShot);

        Assert.IsNotNull(facts.Location);
        Assert.AreEqual(-(33 + 51 / 60.0 + 31.08 / 3600), facts.Location.Latitude, 1e-9, "south is negative");
        Assert.AreEqual(151 + 12 / 60.0 + 29.88 / 3600, facts.Location.Longitude, 1e-9);
        Assert.AreEqual(-15.0, facts.Location.Altitude!.Value, 1e-9, "altitude reference 1 is below sea level");
    }

    [TestMethod]
    public void AClockNeverSet_AZeroDenominator_AndAnEmptyField_SayNothing()
    {
        var block = new ExifBlockBuilder(bigEndian: false);
        block.Root.Add(Ascii(0x010F, "   "));
        block.Root.Add(Ascii(0x0131, "Darktable 4.6"));
        block.Shot.Add(Ascii(0x9003, "0000:00:00 00:00:00"));
        block.Shot.Add(Rational(0x829D, (28, 0)));

        var facts = ExifFacts.Read(block.Build());

        Assert.IsNull(facts.Make, "a field of spaces is no maker");
        Assert.IsNull(facts.Taken, "a camera clock never set is no date");
        Assert.IsNull(facts.FNumber, "a zero denominator is no aperture");
        Assert.AreEqual("Darktable 4.6", facts.Software);
        Assert.IsFalse(facts.DescribesTheShot, "a program's name alone says nothing about the shot");
    }

    [TestMethod]
    public void NoBlock_OrOneThatIsNotTiff_ReadsAsNoFacts()
    {
        Assert.AreSame(CameraFacts.None, ExifFacts.Read(null));
        Assert.AreSame(CameraFacts.None, ExifFacts.Read("not a tiff"u8.ToArray()));
        Assert.AreSame(CameraFacts.None, ExifFacts.Read([(byte)'I', (byte)'I', 42, 0, 0xFF, 0xFF, 0, 0]), "a first directory past the end");
    }

    // ── The colour profile ──────────────────────────────────────────────────

    [TestMethod]
    public void AVersionTwoProfile_IsNamedByItsTextDescription()
        => Assert.AreEqual("sRGB IEC61966-2.1", IccDescription.Read(ProfileV2("sRGB IEC61966-2.1")));

    [TestMethod]
    public void AVersionFourProfile_IsNamedInEnglish_WhereItHasSeveralLanguages()
        => Assert.AreEqual("Display P3", IccDescription.Read(ProfileV4(("fr", "Écran P3"), ("en", "Display P3"))));

    [TestMethod]
    public void AProfileWithNoDescription_OrTooShortToBeOne_HasNoName()
    {
        Assert.IsNull(IccDescription.Read(null));
        Assert.IsNull(IccDescription.Read(new byte[64]));
        Assert.IsNull(IccDescription.Read(new byte[132]), "a profile with no tags");
    }

    // ── Put into words ──────────────────────────────────────────────────────

    [TestMethod]
    public void TheFacts_AreLaidOutUnderHeadings_OnlyWhereThereIsSomethingToSay()
    {
        var details = new PhotoDetails("IMG_0042.jpg", ImageContainer.Jpeg, 2_400_000, null, ExifFacts.Read(Shot(bigEndian: false)), "sRGB IEC61966-2.1");

        var sections = PhotoDetailsSections.Build(details, new SKSizeI(6000, 4000), new SKSizeI(3000, 2000));

        string Value(string section, string label) =>
            sections.Single(s => s.Title == Str.Get(section)).Rows.Single(r => r.Label == Str.Get(label)).Value;

        Assert.AreEqual("6000 × 4000 px · 24 MP", Value("Images.Details.Section.File", "Images.Details.Dimensions"));
        Assert.AreEqual("3000 × 2000 px · 6 MP", Value("Images.Details.Section.File", "Images.Details.SavedSize"), "the size its edits save it at");
        Assert.AreEqual("Canon EOS R6", Value("Images.Details.Section.Camera", "Images.Details.Camera"), "the maker is not said twice");
        Assert.AreEqual("ƒ/2.8", Value("Images.Details.Section.Exposure", "Images.Details.Aperture"));
        Assert.AreEqual("1/250 s", Value("Images.Details.Section.Exposure", "Images.Details.Shutter"));
        Assert.AreEqual("50 mm (80 mm on 35 mm film)", Value("Images.Details.Section.Exposure", "Images.Details.FocalLength"));
        Assert.AreEqual("−0.7 EV", Value("Images.Details.Section.Exposure", "Images.Details.Bias"));
        StringAssert.EndsWith(Value("Images.Details.Section.Taken", "Images.Details.Date"), "(UTC+01:00)");
        Assert.AreEqual("33.85863° S, 151.20830° E", Value("Images.Details.Section.Location", "Images.Details.Position"));
        Assert.AreEqual("15 m below sea level", Value("Images.Details.Section.Location", "Images.Details.Altitude"));
        Assert.AreEqual("sRGB IEC61966-2.1", Value("Images.Details.Section.Colour", "Images.Details.Profile"));

        var bare = PhotoDetailsSections.Build(details with { Camera = CameraFacts.None, ColourProfile = null }, new SKSizeI(10, 10), new SKSizeI(10, 10));
        CollectionAssert.AreEqual(new[] { Str.Get("Images.Details.Section.File") }, bare.Select(s => s.Title).ToArray(),
                                  "a picture with no camera details has only its file to describe");
        Assert.IsFalse(bare[0].Rows.Any(r => r.Label == Str.Get("Images.Details.SavedSize")), "unedited, it saves at the size it is");
    }

    // ── Read from the picture ───────────────────────────────────────────────

    [TestMethod]
    public async Task APictureOnDisk_IsReadThroughTheMetadataItsSaveKeeps()
    {
        var dir = TempDirectory();
        try
        {
            var photo = CopyOfSample(dir, OrientedPhotoSample.FileName);

            var details = await new PhotoDetailsReader(new ImageEditingServices(VirtualFileSystem.Instance).Metadata, VirtualFileSystem.Instance).ReadAsync(photo);

            Assert.AreEqual(OrientedPhotoSample.FileName, details.FileName);
            Assert.AreEqual(ImageContainer.Jpeg, details.Container);
            Assert.AreEqual(new FileInfo(photo).Length, details.FileSize);
            Assert.AreEqual(OrientedPhotoSample.Make, details.Camera.Make);
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { } }
    }

    [TestMethod]
    public void ThePanel_ReadsThePictureOnScreen_OnlyOnceItIsOpen()
    {
        var dir = TempDirectory();
        using var ui = new ViewerUiThread();
        try
        {
            var photo = CopyOfSample(dir, OrientedPhotoSample.FileName);
            var shell = Substitute.For<IShellServices>();
            shell.RunOnUiAsync(Arg.Any<Action>()).Returns(call => ui.Run(call.Arg<Action>()));
            var vm = new ImageViewModel([photo], shell, ImageViewMode.Carousel, new ImageEditingServices(VirtualFileSystem.Instance, new ImageRecipeStore(TimeSpan.FromHours(1))));

            Until(() => ui.Invoke(() => vm.CurrentSession?.Proxy is not null));
            Assert.AreEqual(0, ui.Invoke(() => vm.Details.Sections.Count), "shut, the panel reads nothing");

            ui.Invoke(() => vm.Details.IsOpen = true);
            Until(() => ui.Invoke(() => vm.Details.Sections.Count > 0));

            var sections = ui.Invoke(() => vm.Details.Sections);
            var file = sections.Single(s => s.Title == Str.Get("Images.Details.Section.File"));
            Assert.AreEqual(OrientedPhotoSample.FileName, file.Rows.Single(r => r.Label == Str.Get("Images.Details.Name")).Value);
            Assert.AreEqual($"{OrientedPhotoSample.StoredHeight} × {OrientedPhotoSample.StoredWidth} px · 0 MP",
                            file.Rows.Single(r => r.Label == Str.Get("Images.Details.Dimensions")).Value,
                            "the dimensions are the picture's upright, turned as its orientation says");
            Assert.AreEqual(OrientedPhotoSample.Make,
                            sections.Single(s => s.Title == Str.Get("Images.Details.Section.Camera")).Rows.Single().Value);

            ui.Run(() => vm.Details.CloseCommand.Execute(null)).Wait();
            Assert.IsFalse(ui.Invoke(() => vm.Details.IsOpen));
            vm.Dispose();
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { } }
    }

    private static void Until(Func<bool> condition)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!condition() && clock.Elapsed < TimeSpan.FromSeconds(10)) Thread.Sleep(20);
        Assert.IsTrue(condition(), "timed out");
    }

    // ── Building blocks ─────────────────────────────────────────────────────

    /// <summary>A shot as a camera writes it: who, how, when and where.</summary>
    private static byte[] Shot(bool bigEndian)
    {
        var block = new ExifBlockBuilder(bigEndian);
        block.Root.Add(Ascii(0x010F, "Canon"));
        block.Root.Add(Ascii(0x0110, "Canon EOS R6"));
        block.Root.Add(Ascii(0x013B, "Ada Lovelace"));
        block.Root.Add(Ascii(0x8298, "© 2024 Ada Lovelace"));

        block.Shot.Add(Rational(0x829A, (1, 250)));
        block.Shot.Add(Rational(0x829D, (28, 10)));
        block.Shot.Add(Short(0x8827, 400));
        block.Shot.Add(Ascii(0x9003, "2024:03:15 14:03:22"));
        block.Shot.Add(Ascii(0x9011, "+01:00"));
        block.Shot.Add(SignedRational(0x9204, -7, 10));
        block.Shot.Add(Short(0x9209, 0x19));
        block.Shot.Add(Rational(0x920A, (50, 1)));
        block.Shot.Add(Short(0xA405, 80));
        block.Shot.Add(Ascii(0xA434, "RF24-105mm F4 L IS USM"));

        block.Gps.Add(Ascii(0x0001, "S"));
        block.Gps.Add(Rational(0x0002, (33, 1), (51, 1), (3108, 100)));
        block.Gps.Add(Ascii(0x0003, "E"));
        block.Gps.Add(Rational(0x0004, (151, 1), (12, 1), (2988, 100)));
        block.Gps.Add(new ExifField(0x0005, 1, 1, [1]));
        block.Gps.Add(Rational(0x0006, (15, 1)));
        return block.Build();
    }

    private static ExifField Ascii(ushort tag, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text + "\0");
        return new ExifField(tag, 2, (uint)bytes.Length, bytes);
    }

    private static ExifField Short(ushort tag, ushort value) => new(tag, 3, 1, [(byte)value, (byte)(value >> 8)], LittleEndianNumbers: true);

    private static ExifField Rational(ushort tag, params (uint Numerator, uint Denominator)[] values)
    {
        var bytes = new byte[8 * values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8 * i), values[i].Numerator);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8 * i + 4), values[i].Denominator);
        }
        return new ExifField(tag, 5, (uint)values.Length, bytes, LittleEndianNumbers: true, WordSize: 4);
    }

    private static ExifField SignedRational(ushort tag, int numerator, int denominator)
    {
        var bytes = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, numerator);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), denominator);
        return new ExifField(tag, 10, 1, bytes, LittleEndianNumbers: true, WordSize: 4);
    }

    /// <summary>One directory entry; a numeric value is written little-endian and turned round for a big-endian block, a word at a time.</summary>
    private sealed record ExifField(ushort Tag, ushort Type, uint Count, byte[] Value, bool LittleEndianNumbers = false, int WordSize = 2)
    {
        public byte[] In(bool bigEndian)
        {
            if (!bigEndian || !LittleEndianNumbers) return Value;
            var turned = (byte[])Value.Clone();
            for (int at = 0; at + WordSize <= turned.Length; at += WordSize) Array.Reverse(turned, at, WordSize);
            return turned;
        }
    }

    /// <summary>A TIFF-shaped EXIF block: the first directory, and the EXIF and GPS directories it points to, each value out of line past them where it does not fit in its entry.</summary>
    private sealed class ExifBlockBuilder(bool bigEndian)
    {
        public List<ExifField> Root { get; } = [];
        public List<ExifField> Shot { get; } = [];
        public List<ExifField> Gps { get; } = [];

        public byte[] Build()
        {
            static int Size(int entries) => 2 + 12 * entries + 4;

            int rootEntries = Root.Count + (Shot.Count > 0 ? 1 : 0) + (Gps.Count > 0 ? 1 : 0);
            int shotAt = 8 + Size(rootEntries);
            int gpsAt = shotAt + (Shot.Count > 0 ? Size(Shot.Count) : 0);
            int dataAt = gpsAt + (Gps.Count > 0 ? Size(Gps.Count) : 0);

            var root = new List<ExifField>(Root);
            if (Shot.Count > 0) root.Add(Pointer(0x8769, shotAt));
            if (Gps.Count > 0) root.Add(Pointer(0x8825, gpsAt));

            using var data = new MemoryStream();
            var head = new byte[dataAt];
            (bigEndian ? "MM"u8 : "II"u8).CopyTo(head);
            U16(head, 2, 42);
            U32(head, 4, 8);
            Directory(head, 8, root, data, dataAt);
            if (Shot.Count > 0) Directory(head, shotAt, Shot, data, dataAt);
            if (Gps.Count > 0) Directory(head, gpsAt, Gps, data, dataAt);
            return [.. head, .. data.ToArray()];
        }

        private ExifField Pointer(ushort tag, int at)
        {
            var bytes = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)at);
            return new ExifField(tag, 4, 1, bytes, LittleEndianNumbers: true, WordSize: 4);
        }

        private void Directory(byte[] head, int at, List<ExifField> fields, MemoryStream data, int dataAt)
        {
            U16(head, at, (ushort)fields.Count);
            int entry = at + 2;
            foreach (var field in fields.OrderBy(f => f.Tag))
            {
                var value = field.In(bigEndian);
                U16(head, entry, field.Tag);
                U16(head, entry + 2, field.Type);
                U32(head, entry + 4, field.Count);
                if (value.Length <= 4) value.CopyTo(head, entry + 8);
                else
                {
                    U32(head, entry + 8, (uint)(dataAt + data.Length));
                    data.Write(value);
                    if (data.Length % 2 == 1) data.WriteByte(0);
                }
                entry += 12;
            }
            U32(head, entry, 0);
        }

        private void U16(byte[] b, int at, ushort v)
        {
            if (bigEndian) BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(at), v);
            else BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(at), v);
        }

        private void U32(byte[] b, int at, uint v)
        {
            if (bigEndian) BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(at), v);
            else BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(at), v);
        }
    }

    /// <summary>An ICC profile holding only its description, as a version 2 text description.</summary>
    private static byte[] ProfileV2(string name)
    {
        var text = Encoding.ASCII.GetBytes(name + "\0");
        var tag = new byte[12 + text.Length];
        "desc"u8.CopyTo(tag);
        BinaryPrimitives.WriteUInt32BigEndian(tag.AsSpan(8), (uint)text.Length);
        text.CopyTo(tag, 12);
        return Profile(tag);
    }

    /// <summary>An ICC profile holding only its description, as a version 4 multi-localised string.</summary>
    private static byte[] ProfileV4(params (string Language, string Text)[] names)
    {
        var strings = names.Select(n => Encoding.BigEndianUnicode.GetBytes(n.Text)).ToArray();
        int recordsEnd = 16 + 12 * names.Length;
        var tag = new byte[recordsEnd + strings.Sum(s => s.Length)];
        "mluc"u8.CopyTo(tag);
        BinaryPrimitives.WriteUInt32BigEndian(tag.AsSpan(8), (uint)names.Length);
        BinaryPrimitives.WriteUInt32BigEndian(tag.AsSpan(12), 12);
        int textAt = recordsEnd;
        for (int i = 0; i < names.Length; i++)
        {
            int record = 16 + 12 * i;
            Encoding.ASCII.GetBytes(names[i].Language).CopyTo(tag, record);
            BinaryPrimitives.WriteUInt32BigEndian(tag.AsSpan(record + 4), (uint)strings[i].Length);
            BinaryPrimitives.WriteUInt32BigEndian(tag.AsSpan(record + 8), (uint)textAt);
            strings[i].CopyTo(tag, textAt);
            textAt += strings[i].Length;
        }
        return Profile(tag);
    }

    private static byte[] Profile(byte[] descriptionTag)
    {
        const int tagAt = 128 + 4 + 12;
        var profile = new byte[tagAt + descriptionTag.Length];
        BinaryPrimitives.WriteUInt32BigEndian(profile.AsSpan(128), 1);
        "desc"u8.CopyTo(profile.AsSpan(132));
        BinaryPrimitives.WriteUInt32BigEndian(profile.AsSpan(136), tagAt);
        BinaryPrimitives.WriteUInt32BigEndian(profile.AsSpan(140), (uint)descriptionTag.Length);
        descriptionTag.CopyTo(profile, tagAt);
        return profile;
    }
}
