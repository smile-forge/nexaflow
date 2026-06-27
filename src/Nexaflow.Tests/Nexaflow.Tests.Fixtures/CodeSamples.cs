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
            using System.Collections.Generic;
            using System.Linq;

            namespace Demo;

            /// <summary>Something that can be greeted.</summary>
            public interface IGreeter
            {
                string Greet(string name);
            }

            public abstract class GreeterBase : IGreeter
            {
                protected int Count { get; private set; }
                public event EventHandler? Greeted;

                public abstract string Greet(string name);

                protected void OnGreeted()
                {
                    Count++;
                    Greeted?.Invoke(this, EventArgs.Empty);
                }
            }

            public sealed class Greeter : GreeterBase
            {
                private readonly string _prefix;
                public Greeter(string prefix = "Hello") => _prefix = prefix;

                public override string Greet(string name)
                {
                    OnGreeted();
                    return $"{_prefix}, {name}!";
                }

                public IEnumerable<string> GreetAll(IEnumerable<string> names) => names.Select(Greet);

                public sealed record Result(string Text, int Index);
            }

            public static class Program
            {
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
            from __future__ import annotations

            import math
            from dataclasses import dataclass
            from typing import Iterable


            @dataclass
            class Point:
                x: float
                y: float

                def distance_to(self, other: "Point") -> float:
                    return math.hypot(self.x - other.x, self.y - other.y)


            class Shape:
                def __init__(self, name: str) -> None:
                    self._name = name

                @property
                def name(self) -> str:
                    return self._name

                def area(self) -> float:
                    raise NotImplementedError


            class Circle(Shape):
                def __init__(self, radius: float) -> None:
                    super().__init__("circle")
                    self.radius = radius

                def area(self) -> float:
                    return math.pi * self.radius ** 2


            def total_area(shapes: Iterable[Shape]) -> float:
                return sum(s.area() for s in shapes)


            if __name__ == "__main__":
                print(total_area([Circle(1.0), Circle(2.0)]))
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

        // ── Embedded languages — each hosts another language the editor now injects (see LanguageInjections). ──

        // HTML with inline <style> (css) and <script> (javascript).
        SampleFile.Text("embedded.html",
            """
            <!DOCTYPE html>
            <html lang="en">
            <head>
              <style>
                body { margin: 0; color: #222; }
                .accent { color: #00aa77; }
              </style>
            </head>
            <body>
              <h1 class="accent">Embedded</h1>
              <script>
                function greet(name) {
                  return `Hello, ${name}!`;
                }
                console.log(greet("world"));
              </script>
            </body>
            </html>
            """),

        // Ruby with heredocs: SQL (no grammar → plain), HTML (injected), and nested ERB (ruby + html).
        SampleFile.Text("heredocs.rb",
            """
            # Ruby with embedded languages in heredocs.
            require "erb"

            module Reporting
              class Report
                attr_reader :users

                def initialize(users)
                  @users = users
                end

                def query
                  <<~SQL
                    SELECT id, name FROM users WHERE active = true
                  SQL
                end

                def style
                  <<~CSS
                    .report { color: #222; }
                    .report h1 { font-weight: 600; }
                  CSS
                end

                # A heredoc tagged ERB embeds a template that is itself Ruby-in-HTML.
                def template
                  <<~ERB
                    <h1>Users</h1>
                    <ul>
                    <% @users.each do |u| %>
                      <li><%= u.name %> (<%= u.id %>)</li>
                    <% end %>
                    </ul>
                  ERB
                end
              end
            end
            """),

        // ERB: html content + ruby in <% %> blocks.
        SampleFile.Text("template.erb",
            """
            <h1><%= @title %></h1>
            <ul>
            <% @items.each do |item| %>
              <li><%= item.name %></li>
            <% end %>
            </ul>
            """),

        // Razor: html markup + C# in @code.
        SampleFile.Text("Counter.razor",
            """
            <h1>Counter</h1>
            <p>Current count: @count</p>
            <button @onclick="Increment">Click me</button>

            @code {
                private int count = 0;
                private void Increment() { count++; }
            }
            """),

        // PHP: html text + php code.
        SampleFile.Text("index.php",
            """
            <!DOCTYPE html>
            <html>
            <body>
            <?php
            function greet($name) {
                return "Hello, $name!";
            }
            echo "<h1>" . greet("world") . "</h1>";
            ?>
            </body>
            </html>
            """),

        // Jinja: html + python in {{ }} / {% %} (no jinja grammar; html alias + text scan).
        SampleFile.Text("home.jinja",
            """
            <ul>
            {% for user in users %}
              <li>{{ user.name }} — {{ user.email }}</li>
            {% endfor %}
            </ul>
            """),

        // JavaScript with a GraphQL tagged template (site detected; rendered once a graphql grammar ships).
        SampleFile.Text("query.js",
            """
            import { gql } from "@apollo/client";

            const GET_USER = gql`
              query GetUser($id: ID!) {
                user(id: $id) { id name email }
              }
            `;

            export async function fetchUser(client, id) {
              const { data } = await client.query({ query: GET_USER, variables: { id } });
              return data.user;
            }

            export function formatUser(user) {
              return `${user.name} <${user.email}>`;
            }
            """),

        // Jupyter notebook: json envelope + python in code-cell source (array-of-lines, the format Jupyter
        // writes). The structure extractor decodes each cell, so the class shows in the code map.
        SampleFile.Text("notebook.ipynb",
            """
            {
             "cells": [
              {"cell_type": "markdown", "source": ["# Circles\n"], "metadata": {}},
              {"cell_type": "code", "metadata": {}, "outputs": [], "execution_count": null, "source": [
                 "import math\n",
                 "\n",
                 "class Circle:\n",
                 "    def __init__(self, radius):\n",
                 "        self.radius = radius\n",
                 "\n",
                 "    def area(self):\n",
                 "        return math.pi * self.radius ** 2\n"
              ]},
              {"cell_type": "code", "metadata": {}, "outputs": [], "execution_count": null, "source": [
                 "def total_area(circles):\n",
                 "    return sum(c.area() for c in circles)\n"
              ]}
             ],
             "metadata": {"kernelspec": {"language": "python", "name": "python3"}},
             "nbformat": 4,
             "nbformat_minor": 5
            }
            """),

        // ── Systems / JVM languages (structure extractor) ──

        SampleFile.Text("point.rs",
            """
            // Sample Rust.
            use std::fmt;

            pub struct Point { pub x: i32, pub y: i32 }

            pub trait Shape { fn area(&self) -> f64; }

            impl Point {
                pub fn new(x: i32, y: i32) -> Self { Point { x, y } }
            }

            impl Shape for Point {
                fn area(&self) -> f64 { 0.0 }
            }
            """),

        SampleFile.Text("widget.cpp",
            """
            // Sample C++.
            #include <string>

            namespace ui {
            class Widget {
            public:
                Widget(int id);
                int id() const { return id_; }
            private:
                int id_;
            };
            }
            """),

        SampleFile.Text("Greeter.java",
            """
            // Sample Java.
            package com.example;

            public class Greeter {
                private final String name;
                public Greeter(String name) { this.name = name; }
                public String greet() { return "Hello, " + name + "!"; }
            }
            """),
    ];
}
