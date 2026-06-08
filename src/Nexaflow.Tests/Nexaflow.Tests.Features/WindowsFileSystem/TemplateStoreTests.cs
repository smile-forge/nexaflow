using System;
using System.Collections.Generic;
using System.IO;
using Nexaflow.Features.WindowsFileSystem.FileActions;
using Nexaflow.Features.WindowsFileSystem.Services;

namespace Nexaflow.Tests.Features.WindowsFileSystem;

[TestClass]
public class TemplateStoreTests
{
    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "nexats_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [TestMethod]
    public void SaveTemplates_CopiesSourceAndSetsRelativeName()
    {
        var work = TempDir();
        try
        {
            var src = Path.Combine(work, "source.md");
            File.WriteAllText(src, "BODY");
            var templatesDir = Path.Combine(work, "store");
            var def = new TemplateDefinition { Name = "Doc", FileExtension = ".md", SourcePath = src };

            TemplateStore.SaveTemplates(templatesDir, new List<TemplateDefinition> { def });

            Assert.IsFalse(string.IsNullOrEmpty(def.TemplateFileName));
            Assert.AreEqual(string.Empty, def.SourcePath, "source path should be cleared after copy");
            var stored = Path.Combine(templatesDir, def.TemplateFileName);
            Assert.IsTrue(File.Exists(stored));
            Assert.AreEqual("BODY", File.ReadAllText(stored));
        }
        finally { Directory.Delete(work, true); }
    }

    [TestMethod]
    public void SaveTemplates_PrunesOrphans()
    {
        var work = TempDir();
        try
        {
            var templatesDir = Path.Combine(work, "store");
            Directory.CreateDirectory(templatesDir);
            var orphan = Path.Combine(templatesDir, "orphan.dat");
            File.WriteAllText(orphan, "x");

            TemplateStore.SaveTemplates(templatesDir, new List<TemplateDefinition>());

            Assert.IsFalse(File.Exists(orphan), "unreferenced template file should be pruned");
        }
        finally { Directory.Delete(work, true); }
    }

    [TestMethod]
    public void SaveTemplates_KeepsReferencedStoredFile()
    {
        var work = TempDir();
        try
        {
            var templatesDir = Path.Combine(work, "store");
            Directory.CreateDirectory(templatesDir);
            File.WriteAllText(Path.Combine(templatesDir, "keep.dat"), "x");
            var def = new TemplateDefinition { Name = "Doc", TemplateFileName = "keep.dat" }; // already stored

            TemplateStore.SaveTemplates(templatesDir, new List<TemplateDefinition> { def });

            Assert.IsTrue(File.Exists(Path.Combine(templatesDir, "keep.dat")));
            Assert.AreEqual("keep.dat", def.TemplateFileName);
        }
        finally { Directory.Delete(work, true); }
    }

    [TestMethod]
    public void SaveTemplates_ReplacingSource_DeletesPreviousStoredCopy()
    {
        var work = TempDir();
        try
        {
            var templatesDir = Path.Combine(work, "store");
            Directory.CreateDirectory(templatesDir);
            File.WriteAllText(Path.Combine(templatesDir, "old.dat"), "OLD");

            var src = Path.Combine(work, "newsrc.dat");
            File.WriteAllText(src, "NEW");
            var def = new TemplateDefinition { Name = "Doc", TemplateFileName = "old.dat", SourcePath = src };

            TemplateStore.SaveTemplates(templatesDir, new List<TemplateDefinition> { def });

            Assert.IsFalse(File.Exists(Path.Combine(templatesDir, "old.dat")), "previous copy should be removed");
            Assert.AreEqual("NEW", File.ReadAllText(Path.Combine(templatesDir, def.TemplateFileName)));
        }
        finally { Directory.Delete(work, true); }
    }
}
