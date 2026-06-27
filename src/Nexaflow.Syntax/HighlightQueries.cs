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

        // ── Markup / templating (also the injection hosts) ──────────────────────────────────────
        // HTML colours tags + attributes; the <script>/<style> bodies are left to language injection
        // (javascript/css), so raw_text is intentionally NOT captured here.
        ["html"] =
            """
            (comment) @comment
            (doctype) @keyword
            (tag_name) @tag
            (attribute_name) @attribute
            (quoted_attribute_value) @string
            """,

        // Jinja parses as HTML (the markup) and injects python into {{ }}/{% %} (see LanguageInjections);
        // its native grammar is aliased to html in CodeHighlighter, so this query must be html-valid.
        ["jinja"] =
            """
            (comment) @comment
            (doctype) @keyword
            (tag_name) @tag
            (attribute_name) @attribute
            (quoted_attribute_value) @string
            """,

        ["css"] =
            """
            (comment) @comment
            (tag_name) @tag
            (class_name) @type
            (id_name) @type
            (property_name) @attribute
            (plain_value) @variable
            (integer_value) @number
            (float_value) @number
            (color_value) @constant
            (string_value) @string
            (keyword_query) @keyword
            """,

        // ERB: the only thing to colour directly is the <%# … %> comment; <% code %> → ruby and the
        // surrounding markup → html both come from language injection.
        ["embedded-template"] =
            """
            (comment) @comment
            """,

        // Razor unifies C# and markup in one grammar, so we colour its (C#-shaped) nodes directly rather
        // than injecting. Conservative: only nodes verified present in the razor grammar.
        ["razor"] =
            """
            (identifier) @variable
            ((identifier) @type (#match? @type "^[A-Z]"))
            (razor_comment) @comment
            (predefined_type) @type
            (modifier) @keyword
            (integer_literal) @number
            (string_literal) @string
            (string_literal_content) @string
            (local_function_statement name: (identifier) @function)
            (member_access_expression name: (identifier) @function)
            """,

        // PHP parses its code natively; the surrounding raw HTML (text nodes) is injected as html. Capture
        // string_content (not the whole encapsed_string) so interpolated $vars keep the variable colour.
        ["php"] =
            """
            (name) @variable
            (variable_name) @variable
            (comment) @comment
            (string_content) @string
            (integer) @number
            (float) @number
            (visibility_modifier) @keyword
            (function_definition name: (name) @function)
            (method_declaration name: (name) @function)
            (function_call_expression function: (name) @function)
            [
              "function" "return" "echo" "if" "else" "elseif" "foreach" "while" "for"
              "class" "new" "public" "private" "protected" "static" "use" "namespace"
            ] @keyword
            """,

        // ── Systems / JVM languages (structure extractor + colour) ──────────────────────────────
        ["rust"] =
            """
            (line_comment) @comment
            (block_comment) @comment
            (identifier) @variable
            (type_identifier) @type
            (primitive_type) @type
            (field_identifier) @variable
            (integer_literal) @number
            (float_literal) @number
            (boolean_literal) @constant
            (char_literal) @string
            (string_literal) @string
            (visibility_modifier) @keyword
            (function_item name: (identifier) @function)
            (function_signature_item name: (identifier) @function)
            (call_expression function: (identifier) @function)
            [
              "use" "mod" "struct" "enum" "trait" "impl" "fn" "let" "const" "static"
              "match" "if" "else" "for" "while" "loop" "return" "in" "as" "where" "ref" "move"
            ] @keyword
            """,

        ["cpp"] =
            """
            (comment) @comment
            (identifier) @variable
            (type_identifier) @type
            (namespace_identifier) @type
            (primitive_type) @type
            (field_identifier) @variable
            (number_literal) @number
            (string_literal) @string
            (char_literal) @string
            (access_specifier) @keyword
            (function_declarator declarator: (identifier) @function)
            (function_declarator declarator: (field_identifier) @function)
            [
              "class" "struct" "enum" "namespace" "template" "typename" "using" "return"
              "if" "else" "for" "while" "switch" "case" "break" "continue" "new" "delete"
              "const" "static" "virtual"
            ] @keyword
            """,

        ["java"] =
            """
            (line_comment) @comment
            (block_comment) @comment
            (identifier) @variable
            (type_identifier) @type
            (integral_type) @type
            (void_type) @type
            (decimal_integer_literal) @number
            (string_literal) @string
            (marker_annotation name: (identifier) @attribute)
            (method_declaration name: (identifier) @function)
            (method_invocation name: (identifier) @function)
            (this) @keyword
            [
              "package" "import" "public" "private" "protected" "class" "interface" "enum"
              "extends" "implements" "static" "final" "abstract" "new" "return"
              "if" "else" "for" "while" "switch" "case" "break" "continue" "try" "catch" "finally" "throw" "throws"
            ] @keyword
            """,
    };
}
