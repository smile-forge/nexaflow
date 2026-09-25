using Nexaflow.Markdown.Ast;

namespace Nexaflow.Visuals.Text.Markdown.Code;

/// <summary>
/// Code, read: one run of characters, held as written. What those characters are — keywords, strings, comments — is a
/// grammar's to say and a stage's to hang on it (<see cref="Stages.WithHighlights"/>), and where its lines break is the
/// next stage's (<see cref="Stages.CodeLines"/>).
/// </summary>
public static class CodeParser
{
    public static ContentNode Parse(string? source) =>
        ContentNode.Branch(CodeKinds.Code, [ContentNode.Leaf(Kinds.Verbatim, source ?? string.Empty, Roles.Body)]);
}
