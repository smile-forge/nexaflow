using System.IO;
using LibGit2Sharp;
using Nexaflow.Features.ProductManager.Services;

namespace Nexaflow.Tests.Features.ProductManager;

/// <summary>The git surface used by Take Snapshot: repo detection, tag checks, and commit-and-tag.</summary>
[TestClass]
public class ProductGitTests
{
    private static readonly Signature Sig = new("Tester", "test@example.com", DateTimeOffset.Now);
    private readonly List<string> _temp = [];

    private string InitRepo()
    {
        var dir = Path.Combine(Path.GetTempPath(), "nexaprod_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        Repository.Init(dir);
        _temp.Add(dir);
        return dir;
    }

    private static void Write(string dir, string name, string content)
        => File.WriteAllText(Path.Combine(dir, name), content);

    private static void CommitAll(string dir, string message)
    {
        using var repo = new Repository(dir);
        Commands.Stage(repo, "*");
        repo.Commit(message, Sig, Sig);
    }

    [TestCleanup]
    public void Cleanup()
    {
        foreach (var dir in _temp)
        {
            try
            {
                if (!Directory.Exists(dir)) continue;
                foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                    File.SetAttributes(f, FileAttributes.Normal);
                Directory.Delete(dir, recursive: true);
            }
            catch { /* best-effort */ }
        }
    }

    [TestMethod]
    public void IsRepo_TrueForRepo_FalseForPlainDir()
    {
        var repo = InitRepo();
        Assert.IsTrue(new ProductGit(repo).IsRepo);

        var plain = Path.Combine(Path.GetTempPath(), "nexaplain_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(plain);
        _temp.Add(plain);
        Assert.IsFalse(new ProductGit(plain).IsRepo);
    }

    [TestMethod]
    public void CommitAndTag_CommitsFilesAndCreatesTag()
    {
        var dir = InitRepo();
        Write(dir, "a.txt", "one");
        CommitAll(dir, "init");

        Directory.CreateDirectory(Path.Combine(dir, "docs", "product"));
        var snap = Path.Combine(dir, "docs", "product", "v1.json");
        File.WriteAllText(snap, "{}");

        var (ok, err) = new ProductGit(dir).CommitAndTag([snap], "Product snapshot v1", "v1");

        Assert.IsTrue(ok, err);
        using var repo = new Repository(dir);
        Assert.IsNotNull(repo.Tags["v1"]);
        Assert.AreEqual("Product snapshot v1", repo.Head.Tip.MessageShort);
    }

    [TestMethod]
    public void CommitAndTag_TagAlreadyExists_ReturnsError()
    {
        var dir = InitRepo();
        Write(dir, "a.txt", "one");
        CommitAll(dir, "init");
        using (var repo = new Repository(dir)) repo.ApplyTag("v1");

        var snap = Path.Combine(dir, "v1.json");
        File.WriteAllText(snap, "{}");
        var (ok, err) = new ProductGit(dir).CommitAndTag([snap], "snap", "v1");

        Assert.IsFalse(ok);
        Assert.IsNotNull(err);
    }

    [TestMethod]
    public void HasTag_TrueAfterApply()
    {
        var dir = InitRepo();
        Write(dir, "a.txt", "one");
        CommitAll(dir, "init");
        using (var repo = new Repository(dir)) repo.ApplyTag("v0.4.0");

        Assert.IsTrue(new ProductGit(dir).HasTag("v0.4.0"));
        Assert.IsFalse(new ProductGit(dir).HasTag("v9.9.9"));
    }
}
