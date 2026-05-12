namespace Nexaflow.Features.Projects.Model
{
    public class ProjectInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Scope { get; set; } = string.Empty;
        public List<string> Objectives { get; set; } = [];
        public string? SolutionFile { get; set; }
        public DateTime? LastUpdate { get; set; }
        public List<BacklogItem> Backlog { get; set; } = [];
    }
}
