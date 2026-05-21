namespace Nexaflow.Features.Git.Viewlets;

internal sealed record GitRepoInfo(
    string          BranchName,
    int             StagedCount,
    int             ModifiedCount,
    int             UntrackedCount,
    int?            AheadBy,
    int?            BehindBy,
    string?         LastCommitHash,
    string?         LastCommitMessage,
    DateTimeOffset? LastCommitWhen,
    List<string>    LocalBranches);
