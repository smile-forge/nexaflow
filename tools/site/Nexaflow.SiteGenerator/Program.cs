using System.Text.RegularExpressions;
using Nexaflow.SiteGenerator;

var repo = RepoRoot();
var output = Path.GetFullPath(Named("--output") ?? Path.Combine(repo, "site"));
var version = VersionOf(repo);

// Set by Build-Site.ps1 to the newest release tag when HEAD is NOT sitting on it. The help pages come from the
// working tree, so between releases the site describes a version nobody can download yet, and it has to say so
// rather than let a reader go looking in the release for something that is not in it. The app's version only
// changes when the release is cut, so the version alone cannot tell you this — where HEAD is can.
var aheadOf = Named("--ahead-of");

var corpus = HelpCorpus.Read(repo);
var tree = ProductTree.Read(repo);

Site.Write(corpus, tree, repo, output, version, aheadOf);
Site.CopyImages(repo, output);

Console.WriteLine($"Nexaflow {version} ({tree.Version} product tree): {corpus.Pages.Count} help pages written to {output}");
if (aheadOf is not null)
    Console.WriteLine($"  note: this tree is ahead of {aheadOf} — every page says the download does not have all of it");
foreach (var group in HelpCorpus.GroupOrder)
    Console.WriteLine($"  {group,-18} {corpus.Pages.Count(p => p.Group == group),3}");

return 0;

string? Named(string name)
{
    var at = Array.IndexOf(args, name);
    return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
}

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
