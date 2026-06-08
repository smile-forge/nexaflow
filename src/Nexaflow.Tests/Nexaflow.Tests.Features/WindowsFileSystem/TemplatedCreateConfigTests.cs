using System.Text.Json;
using Nexaflow.Features.WindowsFileSystem.FileActions;

namespace Nexaflow.Tests.Features.WindowsFileSystem;

[TestClass]
public class TemplatedCreateConfigTests
{
    [TestMethod]
    public void Defaults_MatchSpec()
    {
        var cfg = new TemplatedCreateConfig();

        Assert.AreEqual("templatedcreate", cfg.ConfigName);
        Assert.AreEqual("Templated Create", cfg.FriendlyName);
        Assert.AreEqual(0, cfg.Templates.Count);
    }

    [TestMethod]
    public void RoundTrip_PreservesTemplates_DropsSourcePath()
    {
        var cfg = new TemplatedCreateConfig();
        cfg.Templates.Add(new TemplateDefinition
        {
            Name             = "Doc",
            Icon             = "📌",
            FileExtension    = ".md",
            TemplateFileName = "abc.md",
            SourcePath       = @"C:\should\not\persist.md",
        });

        var json = JsonSerializer.Serialize(cfg);
        Assert.IsFalse(json.Contains("persist"), "SourcePath must not be serialized (it is transient).");

        var back = JsonSerializer.Deserialize<TemplatedCreateConfig>(json)!;
        Assert.AreEqual(1, back.Templates.Count);
        Assert.AreEqual("Doc", back.Templates[0].Name);
        Assert.AreEqual("📌", back.Templates[0].Icon);
        Assert.AreEqual(".md", back.Templates[0].FileExtension);
        Assert.AreEqual("abc.md", back.Templates[0].TemplateFileName);
        Assert.AreEqual(string.Empty, back.Templates[0].SourcePath);
    }
}
