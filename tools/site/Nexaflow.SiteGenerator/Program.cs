using System.Text.RegularExpressions;
using Nexaflow.SiteGenerator;

var repo = RepoRoot();
var output = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.Combine(repo, "site");
var version = VersionOf(repo);

var corpus = HelpCorpus.Read(repo);
var tree = ProductTree.Read(repo);

Site.Write(corpus, tree, repo, output, version);
Site.CopyImages(repo, output);

Console.WriteLine($"Nexaflow {version} ({tree.Version} product tree): {corpus.Pages.Count} help pages written to {output}");
foreach (var group in HelpCorpus.GroupOrder)
    Console.WriteLine($"  {group,-18} {corpus.Pages.Count(p => p.Group == group),3}");

return 0;

static string RepoRoot()
{
    for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        if (File.Exists(Path.Combine(dir.FullName, "Nexaflow.slnx")))
            return dir.FullName;

    throw new InvalidOperationException($"no Nexaflow.slnx above '{AppContext.BaseDirectory}'");
}

// The one place the app's version is declared, so the site cannot claim a release that was never cut.
static string VersionOf(string repo)
{
    var csproj = Path.Combine(repo, "src", "Nexaflow.Core", "Nexaflow.Core.csproj");
    var match = Regex.Match(File.ReadAllText(csproj), @"<Version>\s*([^<\s]+)\s*</Version>");

    return match.Success
        ? "v" + match.Groups[1].Value
        : throw new InvalidOperationException($"no <Version> in {csproj}");
}
