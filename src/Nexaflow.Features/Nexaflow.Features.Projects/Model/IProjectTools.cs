namespace Nexaflow.Features.Projects.Model
{
    public interface IProjectTools
    {
        string AbandonTransaction(string transactionId);
        void AddObjectives(string folderName, List<string> objectives);
        Guid AddToDo(string folderName, string title, string? notes = null);
        string AnchorDelete(string transactionId, string anchorName);
        string AnchorInsertAfter(string transactionId, string anchorName, string content);
        string AnchorReplace(string transactionId, string anchorName, string newContent);
        void CancelToDo(string folderName, Guid id);
        void ClearObjectives(string folderName);
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
        void ModifyScope(string folderName, string scope);
        void ModifyToDo(string folderName, Guid id, string? title = null, string? notes = null);
        void ModifyToDoImpPlan(string folderName, Guid id, string impPlan);
        void ModifyToDoTestPlan(string folderName, Guid id, string testPlan);
        void MoveProjectFile(string transactionId, string folderName, string sourceRelativePath, string destinationRelativePath);
        string ProgressToDo(string folderName, Guid id);
        string ReplaceProjectFileContents(string transactionId, string folderName, string relativePath, string oldString, string newString);
        string StartTransaction(string folderName);
        void WriteNewProjectFile(string transactionId, string folderName, string relativePath, string contents);
    }
}