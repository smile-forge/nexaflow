using System.Text;

namespace Nexaflow.Tests.Fixtures;

/// <summary>
/// JSON fixtures for the JSON viewer: a single object, a small array of objects, a deeply nested
/// document, and a large top-level array (1,000 items) that exercises the seek-by-item byte-offset
/// index used for random access into structured items.
/// </summary>
internal sealed class JsonSamples : ISampleSet
{
    public string SubDirectory => "json";

    public IReadOnlyList<SampleFile> Files { get; } =
    [
        SampleFile.Text("object.json",    Object),
        SampleFile.Text("array.json",     Array),
        SampleFile.Text("nested.json",    Nested),
        SampleFile.Text("big_array.json", BuildBigArray()),
    ];

    private const string Object =
        """
        {
          "id": 42,
          "name": "Nexaflow",
          "active": true,
          "tags": ["shell", "wpf", "ai"],
          "version": { "major": 1, "minor": 4, "patch": 0 }
        }
        """;

    private const string Array =
        """
        [
          { "id": 1, "name": "Alice", "role": "admin" },
          { "id": 2, "name": "Bob",   "role": "user" },
          { "id": 3, "name": "Carol", "role": "user" },
          { "id": 4, "name": "Dave",  "role": "guest" }
        ]
        """;

    private const string Nested =
        """
        {
          "company": "Smile Forge",
          "teams": [
            {
              "name": "Platform",
              "members": [
                { "name": "Alice", "skills": ["c#", "wpf"] },
                { "name": "Bob",   "skills": ["ts", "react"] }
              ]
            },
            {
              "name": "AI",
              "members": [
                { "name": "Carol", "skills": ["python", "llm"], "lead": true }
              ]
            }
          ],
          "meta": { "founded": 2021, "remote": true, "offices": null }
        }
        """;

    /// <summary>A 1,000-element array of uniform objects, generated deterministically.</summary>
    private static string BuildBigArray()
    {
        var sb = new StringBuilder(1000 * 64);
        sb.Append("[\n");
        for (int i = 0; i < 1000; i++)
        {
            int score = (i * 7919) % 1000;
            sb.Append("  { \"id\": ").Append(i)
              .Append(", \"label\": \"item-").Append(i)
              .Append("\", \"score\": ").Append(score)
              .Append(", \"even\": ").Append(i % 2 == 0 ? "true" : "false")
              .Append(" }");
            sb.Append(i < 999 ? ",\n" : "\n");
        }
        sb.Append("]\n");
        return sb.ToString();
    }
}
