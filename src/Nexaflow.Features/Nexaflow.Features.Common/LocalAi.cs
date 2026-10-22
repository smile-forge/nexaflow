namespace Nexaflow.Features.Common;

/// <summary>
/// The setup of the models Nexaflow runs itself, for one workspace: the GPUs they run on, where each model the
/// workspace uses runs, and the other language models this machine can run.
/// </summary>
/// <param name="Gpus">The GPUs the AI host runs models on; none when they run on the CPU.</param>
/// <param name="Models">Each part of each model of Nexaflow's own the workspace uses.</param>
/// <param name="Fitting">The language models this machine can run besides the workspace's, by name.</param>
public sealed record LocalAiSetup(IReadOnlyList<LocalAiGpu> Gpus, IReadOnlyList<LocalAiModel> Models, IReadOnlyList<string> Fitting);

/// <summary>A GPU the AI host runs models on.</summary>
/// <param name="Device">Its index among the host's GPUs, as a model's <see cref="LocalAiModel.Device"/> names it.</param>
/// <param name="Name">Its name, as its driver gives it.</param>
/// <param name="TotalBytes">The memory it has.</param>
/// <param name="BudgetBytes">What the AI host lets models hold on it together.</param>
public sealed record LocalAiGpu(int Device, string Name, long TotalBytes, long BudgetBytes);

/// <summary>One part of a model the workspace uses, and where it runs.</summary>
/// <param name="Name">The model's name, with the part's where it has several.</param>
/// <param name="Device">The GPU it runs on; null for the CPU, or for nowhere when <see cref="Fits"/> is false.</param>
/// <param name="Bytes">What it holds on its GPU.</param>
/// <param name="ContextTokens">A language model's context; 0 for any other.</param>
/// <param name="OnDemand">Loaded for each use — a picture's part — rather than kept loaded.</param>
/// <param name="Displaces">Loaded on demand where the models kept loaded on its GPU make way for it while it works.</param>
/// <param name="Fits">Whether it runs on this machine at all.</param>
public sealed record LocalAiModel(string Name, int? Device, long Bytes, int ContextTokens, bool OnDemand, bool Displaces, bool Fits);

/// <summary>What checking the machine came to.</summary>
/// <param name="ReportPath">The report it wrote, in Markdown — for whoever diagnoses the machine, so in English, as
/// the logs are.</param>
/// <param name="Passed">Every model did its work, and no GPU held more than its models were expected to.</param>
public sealed record LocalAiCheck(string ReportPath, bool Passed);

/// <summary>What a check of the machine is doing.</summary>
public enum LocalAiCheckStage { Describing, Loading, Replying, Speaking, Hearing, Drawing, Filming }

/// <summary>Where a check of the machine has got.</summary>
/// <param name="Stage">What it is doing.</param>
/// <param name="Model">The model it is loading, for <see cref="LocalAiCheckStage.Loading"/>.</param>
public sealed record LocalAiCheckProgress(LocalAiCheckStage Stage, string? Model = null);
