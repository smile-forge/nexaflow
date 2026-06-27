namespace Nexaflow.Tests.Fixtures;

/// <summary>
/// Jupyter notebook fixtures for the Notebook feature: a small <c>.ipynb</c> with markdown + multi-line code
/// cells (the array-of-lines source format Jupyter writes), so the cell viewer + per-cell outline have real
/// structure to render.
/// </summary>
internal sealed class NotebookSamples : ISampleSet
{
    public string SubDirectory => "notebook";

    public IReadOnlyList<SampleFile> Files { get; } =
    [
        SampleFile.Text("notebook.ipynb",
            """
            {
             "cells": [
              {"cell_type": "markdown", "source": ["# Circles\n", "\n", "A tiny sample notebook.\n"], "metadata": {}},
              {"cell_type": "code", "metadata": {}, "outputs": [], "execution_count": 1, "source": [
                 "import math\n",
                 "\n",
                 "class Circle:\n",
                 "    def __init__(self, radius):\n",
                 "        self.radius = radius\n",
                 "\n",
                 "    def area(self):\n",
                 "        return math.pi * self.radius ** 2\n"
              ]},
              {"cell_type": "markdown", "source": ["## Helpers\n"], "metadata": {}},
              {"cell_type": "code", "metadata": {}, "outputs": [], "execution_count": 2, "source": [
                 "def total_area(circles):\n",
                 "    return sum(c.area() for c in circles)\n"
              ]}
             ],
             "metadata": {"kernelspec": {"language": "python", "name": "python3"}},
             "nbformat": 4,
             "nbformat_minor": 5
            }
            """),
    ];
}
