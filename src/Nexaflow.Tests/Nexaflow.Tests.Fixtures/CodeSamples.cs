namespace Nexaflow.Tests.Fixtures;

/// <summary>
/// Source/config fixtures for the editor's syntax highlighting: one small, valid file per supported
/// language/format. They drive the highlighting-registry unit tests (extension → engine) and the
/// tree-sitter parse tests (the C# sample). Canonical text samples — LF, UTF-8, no BOM.
/// </summary>
internal sealed class CodeSamples : ISampleSet
{
    public string SubDirectory => "code";

    public IReadOnlyList<SampleFile> Files { get; } =
    [
        SampleFile.Text("hello.cs",
            """
            using System;

            namespace Demo;

            // A tiny sample for the editor's syntax highlighting and parse-tree tests.
            public sealed class Greeter
            {
                public string Greet(string name) => $"Hello, {name}!";

                public static void Main() => Console.WriteLine(new Greeter().Greet("world"));
            }
            """),

        SampleFile.Text("app.js",
            """
            // Sample JavaScript.
            const greet = (name) => `Hello, ${name}!`;
            function main() {
                console.log(greet("world"));
            }
            main();
            """),

        SampleFile.Text("types.ts",
            """
            // Sample TypeScript.
            interface Greeter { greet(name: string): string; }
            const g: Greeter = { greet: (name) => `Hello, ${name}!` };
            console.log(g.greet("world"));
            """),

        SampleFile.Text("main.py",
            """
            # Sample Python.
            def greet(name: str) -> str:
                return f"Hello, {name}!"

            if __name__ == "__main__":
                print(greet("world"))
            """),

        SampleFile.Text("config.ini",
            """
            ; Sample INI configuration.
            [server]
            host = localhost
            port = 8080

            [logging]
            level = info
            """),

        SampleFile.Text("settings.xml",
            """
            <?xml version="1.0" encoding="utf-8"?>
            <settings>
              <!-- Sample XML. -->
              <server host="localhost" port="8080" />
              <logging level="info" />
            </settings>
            """),

        SampleFile.Text("styles.css",
            """
            /* Sample CSS. */
            :root { --accent: #00aa77; }
            body { margin: 0; color: var(--accent); font-family: sans-serif; }
            .button:hover { opacity: 0.8; }
            """),

        SampleFile.Text("page.html",
            """
            <!DOCTYPE html>
            <html lang="en">
            <head><meta charset="utf-8"><title>Sample</title></head>
            <body>
              <h1>Hello</h1>
              <p>Sample HTML for highlighting.</p>
            </body>
            </html>
            """),
    ];
}
