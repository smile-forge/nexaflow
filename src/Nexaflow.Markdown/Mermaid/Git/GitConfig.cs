namespace Nexaflow.Markdown.Mermaid.Git;

/// <summary>
/// What a <c>gitGraph</c> block's front matter asks for: <c>config: gitGraph:</c> — what the branch everything starts on
/// is called and where its lane goes, whether the branches and the commits' ids are shown, whether an id is turned where
/// it is written under its commit, and whether the branches commit side by side rather than one after another — and the
/// <c>themeVariables</c> <c>git0</c>…<c>git7</c> lane colours with the <c>gitBranchLabel0</c>…<c>gitBranchLabel7</c> ink
/// for their labels, and the colours a commit's id and a tag are drawn in.
///
/// <para>A colour or a size nobody wrote is the theme's, so it is null here.</para>
/// </summary>
public sealed record GitConfig
{
    /// <summary>How many lane colours Mermaid's theme has.</summary>
    public const int Lanes = 8;

    /// <summary>What the branch everything starts on is called, where the front matter does not say.</summary>
    public const string Main = "main";

    public static GitConfig Default { get; } = new();

    /// <summary>What the branch everything starts on is called.</summary>
    public string MainBranchName { get; init; } = Main;

    /// <summary>Where the lane of the branch everything starts on goes, among the branches ordering themselves.</summary>
    public double? MainBranchOrder { get; init; }

    /// <summary>Whether the branches are drawn at all — their labels and the lanes they name.</summary>
    public bool ShowBranches { get; init; } = true;

    /// <summary>Whether a commit says its id under it, where one is written for it.</summary>
    public bool ShowCommitLabel { get; init; } = true;

    /// <summary>Whether an id is turned where it is written, as Mermaid turns it, rather than read straight.</summary>
    public bool RotateCommitLabel { get; init; } = true;

    /// <summary>Whether every branch commits side by side, each keeping its own count, rather than one after another.</summary>
    public bool ParallelCommits { get; init; }

    /// <summary>The colour written for each lane, by the number it was written for.</summary>
    public IReadOnlyDictionary<int, string> Lane { get; init; } = new Dictionary<int, string>();

    /// <summary>The ink written for each lane's label, by the number it was written for.</summary>
    public IReadOnlyDictionary<int, string> LaneLabel { get; init; } = new Dictionary<int, string>();

    public string? CommitLabelColour { get; init; }
    public string? CommitLabelBackground { get; init; }
    public double? CommitLabelFontSize { get; init; }

    public string? TagLabelColour { get; init; }
    public string? TagLabelBackground { get; init; }
    public string? TagLabelBorder { get; init; }
    public double? TagLabelFontSize { get; init; }

    /// <summary>Reads the YAML between a block's front-matter fences — see <see cref="MermaidBlock.Config"/>.</summary>
    public static GitConfig Read(string? yaml) => From(MermaidConfig.Read(yaml));

    public static GitConfig From(MermaidConfig config)
    {
        var git = config.Diagram("gitGraph");
        var theme = config.Theme;

        return new GitConfig
        {
            MainBranchName = git.Value("mainBranchName") is { Length: > 0 } name ? name : Main,
            MainBranchOrder = git.Number("mainBranchOrder"),
            ShowBranches = git.Flag("showBranches") ?? true,
            ShowCommitLabel = git.Flag("showCommitLabel") ?? true,
            RotateCommitLabel = git.Flag("rotateCommitLabel") ?? true,
            ParallelCommits = git.Flag("parallelCommits") ?? false,

            Lane = theme.Swatches("git", Lanes, first: 0),
            LaneLabel = theme.Swatches("gitBranchLabel", Lanes, first: 0),
            CommitLabelColour = theme.Value("commitLabelColor"),
            CommitLabelBackground = theme.Value("commitLabelBackground"),
            CommitLabelFontSize = theme.Size("commitLabelFontSize"),
            TagLabelColour = theme.Value("tagLabelColor"),
            TagLabelBackground = theme.Value("tagLabelBackground"),
            TagLabelBorder = theme.Value("tagLabelBorder"),
            TagLabelFontSize = theme.Size("tagLabelFontSize"),
        };
    }

    /// <summary>The colour written for a lane, or null for the theme's.</summary>
    public string? LaneAt(int lane) => Lane.GetValueOrDefault(lane);

    /// <summary>The ink written for a lane's label, or null for the theme's.</summary>
    public string? LaneLabelAt(int lane) => LaneLabel.GetValueOrDefault(lane);
}
