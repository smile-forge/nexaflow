namespace Nexaflow.Tests.Markdown.Mermaid;

/// <summary>
/// The record of what opens a <c>mermaid</c> block: every construct the shared reader knows, and the half-written and
/// wrong things it has to hold without losing. What a new construct is added to.
/// </summary>
internal static class MermaidConstructs
{
    public static readonly (string What, string Source)[] Blocks =
    [
        ("a flowchart", "flowchart TD\n  A --> B\n  B --> C"),
        ("a pie with its title on the header", "pie title Pets\n  \"Dogs\" : 386\n  \"Cats\" : 85"),
        ("front matter with a title and a nested config",
            "---\ntitle: My Chart\nconfig:\n  theme: forest\n  pie:\n    textPosition: 0.5\n---\npie\n  \"A\" : 1\n"),
        ("front matter after blank lines", "\n  \n---\ntitle: \"Quoted\"\n---\ngraph LR\n  a --> b"),
        ("front matter with a list and a comment", "---\n# settings\nconfig:\n  cScale:\n    - red\n    - blue\n---\nradar-beta\n"),
        ("front matter never closed", "---\nconfig:\npie\n"),
        ("empty front matter", "---\n---\npie\n"),
        ("front matter and nothing else", "---\ntitle: x\n---"),
        ("a comment before the header", "%% the first line\n\nsequenceDiagram\n  Alice->>Bob: Hi"),
        ("a directive on one line", "%%{init: {\"theme\": \"dark\"}}%%\ngraph TD\n  a --> b"),
        ("a directive over several lines", "%%{\n  init: {\n    \"theme\": \"forest\"\n  }\n}%%\nflowchart LR\n  a --> b"),
        ("a directive never closed", "%%{init: {\"theme\": \"dark\"}\ngraph TD"),
        ("words after a directive", "%%{init: {}}%% graph TD\npie"),
        ("an accessible title and description", "graph LR\n  accTitle: Big decisions\n  accDescr: Bob's burger stand\n  a --> b"),
        ("a description in braces over lines", "graph LR\n  accDescr {\n    Several\n    lines\n  }\n  a --> b"),
        ("a description in braces never closed", "graph LR\n  accDescr {\n  a --> b"),
        ("an empty accessible title", "pie\n  accTitle:\n"),
        ("a word that only starts like one", "graph LR\n  accTitleX --> b\n  accTitle\n"),
        ("gitGraph with its colon", "gitGraph:\n  commit"),
        ("graph with its semicolon", "graph TD;\n  a-->b;"),
        ("a header naming no known type", "flowchart-elk TD\n  a --> b"),
        ("a header with no keyword", "123\n  a --> b"),
        ("windows line endings", "---\r\ntitle: T\r\n---\r\npie\r\n  \"A\" : 1\r\n"),
        ("a carriage return on its own", "pie\r  \"A\" : 1"),
        ("tabs", "\tgraph\tTD\t\n\t\ta --> b\t"),
        ("nothing at all", ""),
        ("only space", "  \n \n"),
        ("only comments", "%% one\n%% two"),
    ];
}
