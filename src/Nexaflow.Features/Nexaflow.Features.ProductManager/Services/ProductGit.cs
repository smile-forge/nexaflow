using System.IO;
using LibGit2Sharp;

namespace Nexaflow.Features.ProductManager.Services;

/// <summary>
/// The thin git surface the snapshot workflow needs: detect a repo, check a tag, and commit the exported
/// snapshot files + tag them. Read-only / best-effort; never throws for an expected outcome.
/// </summary>
public sealed class ProductGit(string productRoot)
{
    /// <summary>True when the product folder is inside a git working tree.</summary>
    public bool IsRepo
    {
        get { try { return Repository.IsValid(productRoot); } catch { return false; } }
    }

    public bool HasTag(string tag)
    {
        try { using var repo = new Repository(productRoot); return repo.Tags[tag] is not null; }
        catch { return false; }
    }

    /// <summary>
    /// Stages the given files (paths under the repo), commits just them, and creates a tag at that commit.
    /// If nothing changed, tags the current HEAD instead. Returns (false, message) on any failure.
    /// </summary>
    public (bool Ok, string? Error) CommitAndTag(IReadOnlyList<string> files, string message, string tag)
    {
        try
        {
            using var repo = new Repository(productRoot);
            if (repo.Tags[tag] is not null) return (false, $"Tag '{tag}' already exists.");

            foreach (var f in files)
                Commands.Stage(repo, Path.GetRelativePath(productRoot, f).Replace('\\', '/'));

            var sig = repo.Config.BuildSignature(DateTimeOffset.Now)
                      ?? new Signature(Environment.UserName, $"{Environment.UserName}@local", DateTimeOffset.Now);

            Commit commit;
            try { commit = repo.Commit(message, sig, sig); }
            catch (EmptyCommitException) { commit = repo.Head.Tip; }   // export unchanged → tag HEAD as-is
            if (commit is null) return (false, "Nothing to tag (no commits yet).");

            repo.ApplyTag(tag, commit.Sha);
            return (true, null);
        }
        catch (RepositoryNotFoundException) { return (false, "Not a git repository."); }
        catch (LibGit2SharpException ex)     { return (false, ex.Message); }
    }
}
