using System;

namespace Nexaflow.Markdown.Typesetting.Tex;

public sealed class SymbolMappingNotFoundException : Exception
{
    internal SymbolMappingNotFoundException(string symbolName)
        : base($"Cannot find mapping for the symbol with name '{symbolName}'.")
    {
    }
}
