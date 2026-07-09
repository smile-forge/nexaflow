namespace Nexaflow.Features.Projects.Model;

/// <summary>
/// The project operation surface shared by the local ToolManager and the MCP server. Per-project methods
/// take a folder name relative to the active project root.
/// </summary>
public interface IProjectTools
{
    string AbandonTransaction(string transactionId);
    Guid AddToDo(string folderName, string title, string? description = null);
    string AnchorDelete(string transactionId, string anchorName);
    string AnchorInsertAfter(string transactionId, string anchorName, string content);
    string AnchorReplace(string transactionId, string anchorName, string newContent);
    void CancelToDo(string folderName, Guid id);
    void CompleteTransaction(string transactionId);
    void DeleteProjectFile(string transactionId, string folderName, string relativePath);
    void DeleteSummaryForFolder(string transactionId, string folderName, string relativePath);
    List<string> FindAndLabel(string transactionId, string folderName, string relativePath, string searchString, string anchorBaseName);
    void GenerateSummaryForFolder(string transactionId, string folderName, string relativePath, string description);
    object GetPartialProjectFileContents(string folderName, string relativePath, int chunkIndex = 0);
    string GetProjectDetails(string folderName);
    List<string> GetProjectDirectoryFileList(string folderName, string relativePath);
    string GetProjectFileContents(string folderName, string relativePath);
    string GetProjectFileStructure(string folderName);
    List<object> GetProjectList();
    ProjectInfo GetProjectInfo(string folderName);
    string GetToDos(string folderName);
    void ModifyProjectHeader(string folderName, string name, string description);
    void ModifyToDo(string folderName, Guid id, string? title = null, string? description = null);
    void MoveProjectFile(string transactionId, string folderName, string sourceRelativePath, string destinationRelativePath);
    string ProgressToDo(string folderName, Guid id);
    void RemoveToDo(string folderName, Guid id);
    string ReplaceProjectFileContents(string transactionId, string folderName, string relativePath, string oldString, string newString);
    void SetCompletionCriteria(string folderName, List<CompletionCriterion> criteria);
    void SetToDoStatus(string folderName, Guid id, string statusKey);
    string StartTransaction(string folderName);
    void UpgradeSchema(string folderName);
    void WriteNewProjectFile(string transactionId, string folderName, string relativePath, string contents);
}
