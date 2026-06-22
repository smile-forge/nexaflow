using System.Collections.Generic;

namespace Nexaflow.Syntax;

/// <summary>
/// Minimal-but-useful tree-sitter highlight queries per grammar. Deliberately conservative — only node
/// types and reserved-keyword tokens we're confident exist in each grammar — because a single unknown
/// node type makes the whole query fail to compile. Capture names map to theme colours in the editor.
/// </summary>
public static class HighlightQueries
{
    public static IReadOnlyDictionary<string, string> ByGrammar { get; } = new Dictionary<string, string>
    {
        ["c-sharp"] =
            """
            ; Patterns are ordered low → high priority: a later match overrides an earlier one for the
            ; same span, so the catch-all identifier sits first and specific roles win over it.
            (identifier) @variable
            ((identifier) @type (#match? @type "^[A-Z]"))   ; PascalCase ⇒ type/class (incl. usages)

            (comment) @comment

            ; strings: plain/verbatim/raw/char + interpolated literal fragments + the $" / " delimiters.
            ; the interpolation braces stay uncaptured (normal text); the inner identifier stays @variable.
            [(string_literal) (verbatim_string_literal) (raw_string_literal) (character_literal)] @string
            (string_content) @string
            (interpolation_start) @string
            (interpolated_string_expression "\"" @string)

            [(integer_literal) (real_literal)] @number
            (predefined_type) @type
            (boolean_literal) @constant

            ; declaration names + call sites win over the PascalCase heuristic
            (method_declaration       name: (identifier) @function)
            (local_function_statement name: (identifier) @function)
            (invocation_expression function: (identifier) @function)
            (invocation_expression function: (member_access_expression name: (identifier) @function))

            [
              "using" "namespace" "class" "struct" "interface" "enum" "public" "private"
              "protected" "internal" "static" "readonly" "const" "sealed" "abstract"
              "virtual" "override" "params" "ref" "out" "return" "if" "else" "for"
              "foreach" "while" "do" "switch" "case" "break" "continue" "new" "base"
              "is" "as" "in" "default" "try" "catch" "finally" "throw"
            ] @keyword
            """,

        ["json"] =
            """
            (string) @string
            (number) @number
            [(true) (false) (null)] @constant
            """,

        ["python"] =
            """
            (comment) @comment
            (string) @string
            [(integer) (float)] @number
            [
              "def" "class" "return" "if" "elif" "else" "for" "while" "import" "from" "as"
              "with" "try" "except" "finally" "lambda" "pass" "raise" "yield" "global"
              "nonlocal" "assert" "del" "in" "is" "and" "or" "not"
            ] @keyword
            """,

        ["javascript"] =
            """
            (comment) @comment
            (string) @string
            (string_fragment) @string
            (number) @number
            [
              "const" "let" "var" "function" "return" "if" "else" "for" "while" "do" "switch"
              "case" "break" "continue" "class" "extends" "new" "import" "export" "from"
              "default" "typeof" "instanceof" "void" "delete" "throw" "try" "catch" "finally"
            ] @keyword
            """,

        ["typescript"] =
            """
            (comment) @comment
            (string) @string
            (string_fragment) @string
            (number) @number
            (predefined_type) @type
            [
              "const" "let" "var" "function" "return" "if" "else" "for" "while" "class"
              "interface" "enum" "extends" "implements" "new" "import" "export" "from"
              "default" "public" "private" "protected" "readonly" "static" "throw"
              "try" "catch" "finally"
            ] @keyword
            """,

        ["ruby"] =
            """
            (identifier) @variable
            (constant) @type                 ; constants/classes/modules are PascalCase in Ruby
            (comment) @comment
            (string_content) @string
            [(integer) (float)] @number
            (instance_variable) @variable
            (simple_symbol) @constant
            (method name: (identifier) @function)
            (call method: (identifier) @function)
            [
              "def" "class" "module" "end" "if" "elsif" "else" "unless" "while" "until"
              "do" "return" "then" "case" "when" "begin" "rescue" "ensure" "yield"
              "and" "or" "not" "in"
            ] @keyword
            """,
    };
}
