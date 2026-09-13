using System.Collections.Generic;
using System.Linq;
using Nexaflow.Visuals.Text.Markdown.Latex.Tex.Exceptions;
using Nexaflow.Markdown.Ast;

namespace Nexaflow.Visuals.Text.Markdown.Latex.Tex.Parsers.Matrices;

/// <summary>
/// The <c>array</c> environment. Unlike the other matrix-shaped environments it takes an argument -
/// the column preamble - which sits at the front of its body, since <c>\begin</c> hands the whole of
/// what follows the environment name over as one span.
/// </summary>
internal sealed class ArrayCommandParser
{
    internal static readonly ArrayCommandParser Instance = new();

    private ArrayCommandParser()
    {
    }
}
