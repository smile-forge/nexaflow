namespace Nexaflow.Core.FileActions;

public enum MappingSource { Registry, User }

public sealed class ExperienceMapping
{
    public string                      ExperienceId { get; set; } = string.Empty;
    public MappingSource               Source       { get; set; } = MappingSource.User;
    public List<FileSelectionCriteria> Criteria     { get; set; } = [];
}
