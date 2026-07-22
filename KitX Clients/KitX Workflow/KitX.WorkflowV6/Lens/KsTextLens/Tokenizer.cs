namespace KitX.WorkflowV6.Lens.KsTextLens;

// ─────────────────────────────────────────────────────────────────────────────
// Tokenizer — indent-aware KS lexer (discussion notes §十二-A: 4 spaces per level,
// Tab forbidden).
//
// Unlike v5's Superpower token combinator, the v6 tokenizer is line-oriented: it
// emits an Indent token at the start of each non-blank, non-comment line, then the
// rest of that line's tokens. Blank lines and full-line `//` comments are dropped
// (they don't carry indent). Inline `//` comments terminate a line's token stream.
//
// Token kinds:
//   • Indent(n)         — leading-whitespace count / 4, at the start of each logical line
//   • Identifier(s)    — bareword, may be a keyword (resolved by Parser, not here)
//   • StringLiteral(s) — double-quoted, with C#-style escapes; payload is the decoded text
//   • IntegerLiteral(n)
//   • DoubleLiteral(d)
//   • CharLiteral(c)
//   • Boolean: true / false (lexed as Identifier; Parser maps to KsLiteral)
//   • null            (lexed as Identifier; Parser maps to KsLiteral Null)
//   • Pipe            — `>`
//   • Comma           — `,`
//   • Colon           — `:` (used only by switch arms)
//   • LBrace/RBrace   — `{` `}` (used by const/var blocks)
//   • Semicolon       — `;` (optional statement separator; parser treats as no-op)
//   • Placeholder     — `_`
//   • Assign          — `=` (single char only — comparison/arithmetic ops are disabled
//                          per §十二-B, so `==`/`<=`/`>=`/`!=`/`+`/`-`/`*`/`/` are NOT lexed;
//                          they would be illegal and surface as Identifier-or-Error)
//
// Errors emitted into the KsDiagnosticSink:
//   • KS001 Tab character in indentation — at the offending line/column
//   • KS002 Indent not a multiple of 4 — at the offending line/column
//   • KS003 Unterminated string literal
//   • KS004 Unterminated char literal
//   • KS005 Unexpected character (anything not in the grammar's alphabet)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>A token produced by the v6 KS tokenizer.</summary>
internal readonly record struct KsToken
{
    public KsTokenKind Kind { get; init; }
    public string Text { get; init; }
    public int Line { get; init; }
    public int Column { get; init; }
    public int IndentLevel { get; init; }   // only meaningful for Indent tokens
    public object? Value { get; init; }       // decoded payload for literals
}

/// <summary>Discriminant for <see cref="KsToken"/>.</summary>
internal enum KsTokenKind
{
    Indent,
    Identifier,
    StringLiteral,
    IntegerLiteral,
    DoubleLiteral,
    CharLiteral,
    BooleanLiteral,
    NullLiteral,
    Pipe,
    Comma,
    Colon,
    LBrace,
    RBrace,
    LParen,
    RParen,
    Semicolon,
    Placeholder,
    Assign,
    EndOfInput,
}

/// <summary>
/// Indent-aware tokenizer for the v6 KScript grammar. Produces a flat token list
/// (with Indent markers at line starts) consumed by the recursive-descent parser.
/// Pure: the same input always yields the same tokens + diagnostics.
/// </summary>
internal static class Tokenizer
{
    public static (List<KsToken> Tokens, KsDiagnosticSink Diagnostics) Tokenize(string source)
    {
        var tokens = new List<KsToken>();
        var sink = new KsDiagnosticSink();
        if (string.IsNullOrEmpty(source))
        {
            tokens.Add(new KsToken { Kind = KsTokenKind.EndOfInput, Line = 1, Column = 1 });
            return (tokens, sink);
        }

        var lines = source.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
        for (int li = 0; li < lines.Length; li++)
        {
            var line = lines[li];
            var lineNo = li + 1;

            // ── Compute indent ──
            int indentSpaces = 0;
            int idx = 0;
            bool sawTab = false;
            while (idx < line.Length && char.IsWhiteSpace(line[idx]))
            {
                if (line[idx] == '\t')
                {
                    sawTab = true;
                    break;  // reject immediately — Tab anywhere in indent is KS001
                }
                indentSpaces++;
                idx++;
            }

            if (sawTab)
            {
                sink.AddError("KS001", "Tab character is not allowed in indentation; use 4 spaces per level", lineNo, indentSpaces + 1);
                // Skip the whole line — there's no point tokenising past an indent error.
                continue;
            }

            // Skip fully-blank lines and full-line comment lines (no Indent emitted).
            var rest = idx < line.Length ? line[idx..] : string.Empty;
            if (string.IsNullOrWhiteSpace(rest)) continue;
            if (rest.TrimStart().StartsWith("//")) continue;

            // Indent must be a multiple of 4 (§十二-A).
            if (indentSpaces % 4 != 0)
            {
                sink.AddError("KS002",
                    $"Indentation must be a multiple of 4 spaces (got {indentSpaces})", lineNo, 1);
                continue;
            }

            tokens.Add(new KsToken
            {
                Kind = KsTokenKind.Indent,
                IndentLevel = indentSpaces / 4,
                Line = lineNo,
                Column = 1,
            });

            // ── Tokenise the rest of the line ──
            TokenizeLine(rest, lineNo, indentSpaces, tokens, sink);
        }

        tokens.Add(new KsToken { Kind = KsTokenKind.EndOfInput, Line = lines.Length, Column = 1 });
        return (tokens, sink);
    }

    private static void TokenizeLine(string line, int lineNo, int indentSpaces,
        List<KsToken> tokens, KsDiagnosticSink sink)
    {
        int i = 0;
        int columnBase = indentSpaces + 1;  // 1-based column offset for tokens on this line
        while (i < line.Length)
        {
            char c = line[i];
            // Skip intra-line spaces (not tabs — those would be a parse error mid-line too).
            if (c == ' ') { i++; continue; }
            if (c == '\t')
            {
                sink.AddError("KS001", "Tab character is not allowed; use spaces", lineNo, columnBase + i);
                i++;
                continue;
            }

            // Inline comment terminates the line.
            if (c == '/' && i + 1 < line.Length && line[i + 1] == '/')
            {
                break;  // rest of line is a comment
            }

            int col = columnBase + i;

            // String literal
            if (c == '"')
            {
                var (str, next) = ReadString(line, i, lineNo, col, sink);
                if (str is not null)
                {
                    tokens.Add(new KsToken { Kind = KsTokenKind.StringLiteral, Text = str, Value = str, Line = lineNo, Column = col });
                }
                i = next;
                continue;
            }

            // Char literal
            if (c == '\'')
            {
                var (ch, next) = ReadChar(line, i, lineNo, col, sink);
                if (ch is not null)
                {
                    tokens.Add(new KsToken { Kind = KsTokenKind.CharLiteral, Text = ch.Value.ToString(), Value = ch, Line = lineNo, Column = col });
                }
                i = next;
                continue;
            }

            // Number literal (integer or double)
            if (char.IsDigit(c) || (c == '-' && i + 1 < line.Length && char.IsDigit(line[i + 1])))
            {
                var (num, next) = ReadNumber(line, i, lineNo, col, sink);
                tokens.Add(num);
                i = next;
                continue;
            }

            // Identifier or keyword
            if (char.IsLetter(c) || c == '_')
            {
                int start = i;
                while (i < line.Length && (char.IsLetterOrDigit(line[i]) || line[i] == '_')) i++;
                var word = line[start..i];
                var kind = ClassifyWord(word);
                tokens.Add(new KsToken
                {
                    Kind = kind,
                    Text = word,
                    Value = kind == KsTokenKind.BooleanLiteral ? bool.Parse(word)
                         : kind == KsTokenKind.NullLiteral ? null
                         : (object?)word,
                    Line = lineNo,
                    Column = col,
                });
                continue;
            }

            // Punctuation
            switch (c)
            {
                case '>':
                    tokens.Add(new KsToken { Kind = KsTokenKind.Pipe, Text = ">", Line = lineNo, Column = col });
                    i++;
                    continue;
                case ',':
                    tokens.Add(new KsToken { Kind = KsTokenKind.Comma, Text = ",", Line = lineNo, Column = col });
                    i++;
                    continue;
                case ':':
                    tokens.Add(new KsToken { Kind = KsTokenKind.Colon, Text = ":", Line = lineNo, Column = col });
                    i++;
                    continue;
                case '{':
                    tokens.Add(new KsToken { Kind = KsTokenKind.LBrace, Text = "{", Line = lineNo, Column = col });
                    i++;
                    continue;
                case '}':
                    tokens.Add(new KsToken { Kind = KsTokenKind.RBrace, Text = "}", Line = lineNo, Column = col });
                    i++;
                    continue;
                case '(':
                    tokens.Add(new KsToken { Kind = KsTokenKind.LParen, Text = "(", Line = lineNo, Column = col });
                    i++;
                    continue;
                case ')':
                    tokens.Add(new KsToken { Kind = KsTokenKind.RParen, Text = ")", Line = lineNo, Column = col });
                    i++;
                    continue;
                case ';':
                    tokens.Add(new KsToken { Kind = KsTokenKind.Semicolon, Text = ";", Line = lineNo, Column = col });
                    i++;
                    continue;
                case '=':
                    tokens.Add(new KsToken { Kind = KsTokenKind.Assign, Text = "=", Line = lineNo, Column = col });
                    i++;
                    continue;
                default:
                    sink.AddError("KS005", $"Unexpected character '{c}'", lineNo, col);
                    i++;
                    continue;
            }
        }
    }

    private static KsTokenKind ClassifyWord(string word) => word switch
    {
        "true" or "false" => KsTokenKind.BooleanLiteral,
        "null" => KsTokenKind.NullLiteral,
        "_" => KsTokenKind.Placeholder,
        _ => KsTokenKind.Identifier,
    };

    private static (string?, int) ReadString(string line, int i, int lineNo, int col, KsDiagnosticSink sink)
    {
        // i points at the opening quote.
        var sb = new System.Text.StringBuilder();
        int j = i + 1;
        while (j < line.Length)
        {
            char c = line[j];
            if (c == '\\')
            {
                if (j + 1 >= line.Length)
                {
                    sink.AddError("KS003", "Unterminated string literal", lineNo, col);
                    return (null, line.Length);
                }
                // Decode common C# escapes.
                char esc = line[j + 1];
                sb.Append(esc switch
                {
                    'n' => '\n',
                    't' => '\t',
                    'r' => '\r',
                    '\\' => '\\',
                    '"' => '"',
                    '\'' => '\'',
                    '0' => '\0',
                    _ => esc,  // unknown escapes pass through verbatim
                });
                j += 2;
                continue;
            }
            if (c == '"')
            {
                return (sb.ToString(), j + 1);
            }
            sb.Append(c);
            j++;
        }
        sink.AddError("KS003", "Unterminated string literal", lineNo, col);
        return (null, line.Length);
    }

    private static (char?, int) ReadChar(string line, int i, int lineNo, int col, KsDiagnosticSink sink)
    {
        // i points at the opening quote.
        int j = i + 1;
        if (j >= line.Length)
        {
            sink.AddError("KS004", "Unterminated char literal", lineNo, col);
            return (null, line.Length);
        }
        char first = line[j];
        if (first == '\\')
        {
            if (j + 2 >= line.Length || line[j + 2] != '\'')
            {
                sink.AddError("KS004", "Unterminated char literal", lineNo, col);
                return (null, line.Length);
            }
            char esc = line[j + 1];
            char decoded = esc switch
            {
                'n' => '\n',
                't' => '\t',
                'r' => '\r',
                '\\' => '\\',
                '\'' => '\'',
                '"' => '"',
                '0' => '\0',
                _ => esc,
            };
            return (decoded, j + 3);
        }
        if (j + 1 >= line.Length || line[j + 1] != '\'')
        {
            sink.AddError("KS004", "Unterminated char literal", lineNo, col);
            return (null, line.Length);
        }
        return (first, j + 2);
    }

    private static (KsToken, int) ReadNumber(string line, int i, int lineNo, int col, KsDiagnosticSink sink)
    {
        int start = i;
        bool isDouble = false;
        if (line[i] == '-') i++;
        while (i < line.Length && (char.IsDigit(line[i]) || line[i] == '.'))
        {
            if (line[i] == '.') isDouble = true;
            i++;
        }
        var text = line[start..i];
        if (isDouble)
        {
            if (double.TryParse(text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var d))
            {
                return (new KsToken { Kind = KsTokenKind.DoubleLiteral, Text = text, Value = d, Line = lineNo, Column = col }, i);
            }
            sink.AddError("KS006", $"Malformed double literal: {text}", lineNo, col);
            return (new KsToken { Kind = KsTokenKind.DoubleLiteral, Text = text, Value = 0.0, Line = lineNo, Column = col }, i);
        }
        if (int.TryParse(text, out var n))
        {
            return (new KsToken { Kind = KsTokenKind.IntegerLiteral, Text = text, Value = n, Line = lineNo, Column = col }, i);
        }
        sink.AddError("KS006", $"Malformed integer literal: {text}", lineNo, col);
        return (new KsToken { Kind = KsTokenKind.IntegerLiteral, Text = text, Value = 0, Line = lineNo, Column = col }, i);
    }
}