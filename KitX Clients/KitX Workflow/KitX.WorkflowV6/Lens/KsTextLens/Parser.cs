namespace KitX.WorkflowV6.Lens.KsTextLens;

using KitX.WorkflowV6.Ir.Ast;

// ─────────────────────────────────────────────────────────────────────────────
// Parser — recursive-descent parser for the v6 indented KS grammar.
//
// Replaces the v5 Superpower token-combinator parser. Indented grammars (Python
// style) don't compose well with token combinators — the combinator library wants
// to look-ahead by tokens, but indent/dedent are *line-level* events. A hand-rolled
// recursive-descent parser with an indent stack is the standard solution and is
// what the implementation plan §Phase 2 prescribes.
//
// Grammar (informal — full grammar in KScriptGrammarRule.md v6.0):
//
//   program        ::= declBlock* statement*
//   declBlock      ::= ('const' | 'var') '{' declRow* '}'
//   declRow        ::= type name ('=' expr)?    // on one line
//   statement      ::= ifStmt | switchStmt | forEachStmt | whileStmt
//                    | break | continue | exit ('(' ')')?
//                    | pipeline
//   ifStmt         ::= 'if' condition INDENT statement+ DEDENT
//                      ('else' (ifStmt | INDENT statement+ DEDENT))?
//   switchStmt     ::= 'switch' expr INDENT arm+ DEDENT
//   arm            ::= (integer | 'default') ':' statement+ (inline or block)
//   forEachStmt    ::= 'forEach' expr 'as' name INDENT statement+ DEDENT
//   whileStmt      ::= 'while' condition INDENT statement+ DEDENT
//   pipeline       ::= expr (',' expr)* ('>' segment)* ('=' name)? ';'?
//   segment        ::= name '(' (funcArg (',' funcArg)*)? ')' | name
//   condition      ::= expr (',' expr)* ('>' segment)*
//                      // simple form 'if cond' returns expr directly;
//                      // pipeline form 'if a, b > Func(...)' returns KsPipeline
//   expr           ::= literal | '_' | identifier | name '(' (funcArg (',' funcArg)*)? ')'
//   funcArg        ::= literal | '_'    // v6.0 rule: parens may only contain
//                                        // literals/placeholders; non-literal values
//                                        // MUST use pipeline sources
//   literal        ::= string | integer | double | char | true | false | null
//
// INDENT/DEDENT are not real tokens — the parser tracks the indent level of the
// current line and treats a higher level as "enter body", a lower level as "exit
// body". parseBody(currentLevel) consumes statements while their indent is
// greater than currentLevel.
//
// The parser is recursive descent with no backtracking (each lookahead token
// unambiguously picks a rule). Errors are collected into the KsDiagnosticSink and
// the parser recovers as best it can.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Recursive-descent parser for the v6 indented KS grammar. Produces a
/// <see cref="KsProgram"/> AST. Pure: the same tokens always yield the same AST.
/// </summary>
internal sealed class Parser
{
    private readonly List<KsToken> _tokens;
    private readonly KsDiagnosticSink _sink;
    private int _pos;

    private Parser(List<KsToken> tokens, KsDiagnosticSink sink)
    {
        _tokens = tokens;
        _sink = sink;
        _pos = 0;
    }

    /// <summary>Parses a token list into a <see cref="KsProgram"/> AST.</summary>
    public static (KsProgram Program, KsDiagnosticSink Diagnostics) Parse(List<KsToken> tokens, KsDiagnosticSink sink)
    {
        var parser = new Parser(tokens, sink);
        var program = parser.ParseProgram();
        return (program, parser._sink);
    }

    // ── Token helpers ──

    private KsToken Current => _tokens[_pos];
    private KsToken Peek(int offset = 0) =>
        _pos + offset < _tokens.Count ? _tokens[_pos + offset] : _tokens[^1];

    private bool AtEnd => Current.Kind == KsTokenKind.EndOfInput;

    private KsToken Advance()
    {
        var t = Current;
        if (!AtEnd) _pos++;
        return t;
    }

    private bool Match(KsTokenKind kind)
    {
        if (Current.Kind == kind) { Advance(); return true; }
        return false;
    }

    private bool IsKeyword(string word) =>
        Current.Kind == KsTokenKind.Identifier && Current.Text == word;

    private bool MatchKeyword(string word)
    {
        if (IsKeyword(word)) { Advance(); return true; }
        return false;
    }

    /// <summary>
    /// If the current token is a <see cref="KsTokenKind.Comment"/>, consumes it and
    /// returns its (trimmed) text; otherwise returns null without advancing.
    /// </summary>
    private string? TryConsumeComment()
    {
        if (Current.Kind == KsTokenKind.Comment)
        {
            var text = Current.Text;
            Advance();
            return text;
        }
        return null;
    }

    private void Error(string code, string message, KsToken? at = null)
    {
        var t = at ?? Current;
        _sink.AddError(code, message, t.Line, t.Column);
    }

    // ── Program ──

    private KsProgram ParseProgram()
    {
        var body = ImmutableArray.CreateBuilder<KsStatement>();
        KsConstBlock? constBlock = null;
        KsVarBlock? varBlock = null;
        List<string>? pendingLeading = null;  // accumulated full-line comments for the next statement

        while (!AtEnd)
        {
            // Find the next Indent token at level 0.
            if (Current.Kind != KsTokenKind.Indent) { Advance(); continue; }
            var indent = Current.IndentLevel;
            if (indent != 0)
            {
                Error("KS010", $"Top-level statement must be at indent 0 (got {indent})");
                Advance();  // consume the wrong-level Indent to avoid infinite loop
                while (!AtEnd && Current.Kind != KsTokenKind.Indent) Advance();
                continue;
            }
            Advance();  // consume Indent(0)

            // A full-line comment emits Indent(0) + Comment. Accumulate it as a leading
            // comment for the next top-level statement.
            if (Current.Kind == KsTokenKind.Comment)
            {
                (pendingLeading ??= new()).Add(Current.Text);
                Advance();
                continue;
            }

            // Comments directly above a const/var block are out of scope (declaration-
            // block comment preservation is deferred); drop pending there.
            if (MatchKeyword("const"))
            {
                pendingLeading = null;
                if (constBlock is not null)
                    Error("KS011", "Duplicate const block");
                constBlock = ParseConstBlock();
                continue;
            }
            if (MatchKeyword("var"))
            {
                pendingLeading = null;
                if (varBlock is not null)
                    Error("KS011", "Duplicate var block");
                varBlock = ParseVarBlock();
                continue;
            }
            var stmt = ParseStatement();
            if (pendingLeading is { Count: > 0 })
            {
                stmt.LeadingComment = string.Join("\n", pendingLeading);
                pendingLeading = null;
            }
            body.Add(stmt);
        }

        return new KsProgram
        {
            ConstBlock = constBlock,
            VarBlock = varBlock,
            Body = body.ToImmutable(),
            SourceLine = 1,
        };
    }

    // ── Decl blocks ──

    private KsConstBlock ParseConstBlock()
    {
        var decls = ImmutableArray.CreateBuilder<KsConstDecl>();
        ExpectLBrace();
        while (!AtEnd && Current.Kind != KsTokenKind.RBrace)
        {
            // Each decl row lives on its own line, prefixed by an Indent token.
            if (Current.Kind == KsTokenKind.Indent) Advance();
            if (Current.Kind == KsTokenKind.RBrace) break;
            decls.Add(ParseConstRow());
            // Skip any remaining tokens on this line.
            while (!AtEnd && Current.Kind != KsTokenKind.Indent
                          && Current.Kind != KsTokenKind.RBrace) Advance();
        }
        Match(KsTokenKind.RBrace);
        return new KsConstBlock { Declarations = decls.ToImmutable() };
    }

    private KsVarBlock ParseVarBlock()
    {
        var decls = ImmutableArray.CreateBuilder<KsVarDecl>();
        ExpectLBrace();
        while (!AtEnd && Current.Kind != KsTokenKind.RBrace)
        {
            if (Current.Kind == KsTokenKind.Indent) Advance();
            if (Current.Kind == KsTokenKind.RBrace) break;
            decls.Add(ParseVarRow());
            while (!AtEnd && Current.Kind != KsTokenKind.Indent
                          && Current.Kind != KsTokenKind.RBrace) Advance();
        }
        Match(KsTokenKind.RBrace);
        return new KsVarBlock { Declarations = decls.ToImmutable() };
    }

    private KsConstDecl ParseConstRow()
    {
        var (typeTok, nameTok, initExpr, src) = ParseDeclRowCore();
        return new KsConstDecl
        {
            Name = nameTok.Text,
            Type = typeTok.Text,
            InitialValueExpression = initExpr,
            SourceText = src,
            SourceLine = typeTok.Line,
        };
    }

    private KsVarDecl ParseVarRow()
    {
        var (typeTok, nameTok, initExpr, src) = ParseDeclRowCore();
        return new KsVarDecl
        {
            Name = nameTok.Text,
            Type = typeTok.Text,
            InitialValueExpression = initExpr,
            SourceText = src,
            SourceLine = typeTok.Line,
        };
    }

    private (KsToken typeTok, KsToken nameTok, string? initExpr, string src) ParseDeclRowCore()
    {
        // Form: <type> <name> ['=' <expr-text>]
        var typeTok = Current.Kind == KsTokenKind.Identifier ? Advance() : Current;
        var nameTok = Current.Kind == KsTokenKind.Identifier ? Advance() : Current;
        if (typeTok.Kind != KsTokenKind.Identifier)
            Error("KS012", "Declaration must start with a type name", typeTok);
        if (nameTok.Kind != KsTokenKind.Identifier)
            Error("KS012", "Declaration must have a name after the type", nameTok);

        string? initExpr = null;
        if (Match(KsTokenKind.Assign))
        {
            // Capture the rest of the line as the initialiser expression text.
            int start = _pos;
            while (!AtEnd && Current.Kind != KsTokenKind.Indent
                          && Current.Kind != KsTokenKind.RBrace) Advance();
            initExpr = ReconstructText(_tokens, start, _pos).Trim();
        }

        var src = $"{typeTok.Text} {nameTok.Text}{(initExpr is null ? "" : " = " + initExpr)}";
        return (typeTok, nameTok, initExpr, src);
    }

    private void ExpectLBrace()
    {
        if (!Match(KsTokenKind.LBrace))
            Error("KS013", "Expected '{' after const/var");
    }

    private static string ReconstructText(List<KsToken> tokens, int from, int toExclusive)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = from; i < toExclusive && i < tokens.Count; i++)
        {
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(tokens[i].Text);
        }
        return sb.ToString();
    }

    // ── Statements ──

    private KsStatement ParseStatement()
    {
        // Current is the first token of the statement (the Indent was consumed).
        switch (Current.Kind)
        {
            case KsTokenKind.Identifier:
                return Current.Text switch
                {
                    "if" => ParseIf(),
                    "switch" => ParseSwitch(),
                    "forEach" => ParseForEach(),
                    "while" => ParseWhile(),
                    "break" => ParseBreak(),
                    "continue" => ParseContinue(),
                    "exit" => ParseExit(),
                    _ => ParsePipelineOrAssignment(),
                };
            default:
                return ParsePipelineOrAssignment();
        }
    }

    private KsIf ParseIf()
    {
        var ifTok = Advance();  // 'if'
        var (cond, lastSegComment) = ParseHeaderPipelineExpression();
        if (!Match(KsTokenKind.Colon))
            Error("KS063", "Expected ':' after if-header expression");
        var trailing = TryConsumeComment();  // post-colon inline comment
        string? stmtTrailing = null;
        var lastComment = lastSegComment ?? trailing;
        if (lastComment is not null && cond is KsPipeline pipe)
            pipe.Segments[^1].Comment = lastComment;
        else
            stmtTrailing = trailing;
        int keywordIndent = LastConsumedIndentLevel();
        var thenBody = ParseBody(keywordIndent + 1, $"if on line {ifTok.Line}");
        ImmutableArray<KsStatement> elseBody = [];

        // `else` should sit at the same indent level as the `if`. After ParseBody
        // returns, the current token should be an Indent at keywordIndent (because
        // ParseBody stops when it sees a lower indent). Check whether that Indent
        // is followed by the `else` keyword.
        if (Current.Kind == KsTokenKind.Indent && Current.IndentLevel == keywordIndent)
        {
            // Peek one token ahead: is it `else`?
            if (Peek(1).Kind == KsTokenKind.Identifier && Peek(1).Text == "else")
            {
                Advance();  // consume Indent(keywordIndent)
                var elseTok = Advance();  // consume 'else'
                if (IsKeyword("if"))
                {
                    // `else if` → single nested If as the only statement of else body.
                    elseBody = [ParseIf()];
                }
                else
                {
                    if (!Match(KsTokenKind.Colon))
                        Error("KS063", "Expected ':' after 'else'");
                    TryConsumeComment();  // post-colon comment on `else:` line (not attached)
                    elseBody = ParseBody(keywordIndent + 1, $"else on line {elseTok.Line}");
                }
            }
        }

        return new KsIf
        {
            Condition = cond,
            ThenBody = thenBody,
            ElseBody = elseBody,
            SourceLine = ifTok.Line,
            TrailingComment = stmtTrailing,
        };
    }

    private KsSwitch ParseSwitch()
    {
        var swTok = Advance();  // 'switch'
        var selector = ParseExpression();
        if (!Match(KsTokenKind.Colon))
            Error("KS063", "Expected ':' after switch selector");
        TryConsumeComment();  // post-colon comment on the `switch` header line (not attached)
        int keywordIndent = LastConsumedIndentLevel();
        int armIndent = keywordIndent + 1;
        var arms = ImmutableArray.CreateBuilder<ImmutableArray<KsStatement>>();
        ImmutableArray<KsStatement> defaultBody = [];
        bool sawDefault = false;

        while (Current.Kind == KsTokenKind.Indent && Current.IndentLevel == armIndent)
        {
            Advance();  // consume Indent(armIndent)
            if (MatchKeyword("default"))
            {
                if (sawDefault) Error("KS022", "Duplicate default arm");
                sawDefault = true;
                if (!Match(KsTokenKind.Colon))
                    Error("KS020", "Expected ':' after 'default'");
                defaultBody = ParseArmBody(armIndent);
            }
            else if (Current.Kind == KsTokenKind.IntegerLiteral)
            {
                Advance();
                if (!Match(KsTokenKind.Colon))
                    Error("KS020", "Expected ':' after case label");
                arms.Add(ParseArmBody(armIndent));
            }
            else
            {
                Error("KS021", "Expected case label or 'default' in switch arm");
                while (!AtEnd && Current.Kind != KsTokenKind.Indent) Advance();
            }
        }

        return new KsSwitch
        {
            Selector = selector,
            Arms = arms.ToImmutable(),
            Default = defaultBody,
            SourceLine = swTok.Line,
        };
    }

    private ImmutableArray<KsStatement> ParseArmBody(int armIndent)
    {
        // An arm body is either:
        //   (a) inline — more tokens follow the ':' on the same line
        //   (b) a block — statements at armIndent + 1
        if (Current.Kind != KsTokenKind.Indent && Current.Kind != KsTokenKind.EndOfInput)
        {
            // Inline: parse the rest of the line as one statement.
            return [ParseStatement()];
        }
        // Block at armIndent + 1.
        return ParseBody(armIndent + 1, "switch arm");
    }

    private KsForEach ParseForEach()
    {
        var feTok = Advance();  // 'forEach'
        // Source accepts pipeline expressions (like if/while conditions), so
        // `forEach loopMax > Range(0, _, 1) as i` is valid — the entire pipeline
        // between `forEach` and `as` is the source expression.
        var (source, lastSegComment) = ParseHeaderPipelineExpression();
        if (!MatchKeyword("as"))
            Error("KS030", "Expected 'as' after forEach source");
        if (Current.Kind != KsTokenKind.Identifier)
            Error("KS031", "Expected item name after 'as'");
        var itemName = Advance().Text;
        if (!Match(KsTokenKind.Colon))
            Error("KS063", "Expected ':' after forEach header");
        var trailing = TryConsumeComment();
        string? stmtTrailing = null;
        var lastComment = lastSegComment ?? trailing;
        if (lastComment is not null && source is KsPipeline pipe)
            pipe.Segments[^1].Comment = lastComment;
        else
            stmtTrailing = trailing;
        int keywordIndent = LastConsumedIndentLevel();
        var body = ParseBody(keywordIndent + 1, $"forEach on line {feTok.Line}");
        return new KsForEach
        {
            Source = source,
            ItemName = itemName,
            Body = body,
            SourceLine = feTok.Line,
            TrailingComment = stmtTrailing,
        };
    }

    private KsWhile ParseWhile()
    {
        var whTok = Advance();  // 'while'
        var (cond, lastSegComment) = ParseHeaderPipelineExpression();
        if (!Match(KsTokenKind.Colon))
            Error("KS063", "Expected ':' after while-header expression");
        var trailing = TryConsumeComment();
        string? stmtTrailing = null;
        var lastComment = lastSegComment ?? trailing;
        if (lastComment is not null && cond is KsPipeline pipe)
            pipe.Segments[^1].Comment = lastComment;
        else
            stmtTrailing = trailing;
        int keywordIndent = LastConsumedIndentLevel();
        var body = ParseBody(keywordIndent + 1, $"while on line {whTok.Line}");
        return new KsWhile
        {
            Condition = cond,
            Body = body,
            SourceLine = whTok.Line,
            TrailingComment = stmtTrailing,
        };
    }

    private KsBreak ParseBreak()
    {
        var t = Advance();
        Match(KsTokenKind.Semicolon);
        var trailing = TryConsumeComment();
        var stmt = new KsBreak { SourceLine = t.Line, SourceText = "break" };
        stmt.TrailingComment = trailing;
        return stmt;
    }

    private KsContinue ParseContinue()
    {
        var t = Advance();
        Match(KsTokenKind.Semicolon);
        var trailing = TryConsumeComment();
        var stmt = new KsContinue { SourceLine = t.Line, SourceText = "continue" };
        stmt.TrailingComment = trailing;
        return stmt;
    }

    private KsExit ParseExit()
    {
        var t = Advance();
        // Tolerate `exit()` — consume the parens if present.
        if (Match(KsTokenKind.LParen)) Match(KsTokenKind.RParen);
        Match(KsTokenKind.Semicolon);
        var trailing = TryConsumeComment();
        var stmt = new KsExit { SourceLine = t.Line, SourceText = "exit" };
        stmt.TrailingComment = trailing;
        return stmt;
    }

    // ── Pipelines and expressions ──

    private KsStatement ParsePipelineOrAssignment()
    {
        // A pipeline line: <src> (',' <src>)* ('>' <segment>)* ('=' <name>)? ';'?
        // A bare call:    <call>   (lowered to a one-source pipeline with one call segment)
        //
        // forEach is NOT a valid pipeline segment target — it is a prefix keyword
        // statement: `forEach <source> as <item>`. Use that form instead.
        var sources = ImmutableArray.CreateBuilder<KsNode>();
        sources.Add(ParseExpression());
        while (Match(KsTokenKind.Comma))
            sources.Add(ParseExpression());

        var segments = ImmutableArray.CreateBuilder<KsPipelineSegment>();
        while (Match(KsTokenKind.Pipe))
        {
            if (IsKeyword("forEach"))
            {
                Error("KS061", "'forEach' is not valid as a pipeline segment. Use prefix form: 'forEach <source> as <item>'");
                break;
            }
            segments.Add(ParseSegment());
        }

        // Capture point A — a trailing comment after the inline sources/segments. In a
        // multi-line pipeline this sits on the source line (e.g. `a, b // src cmt`); in
        // a single-line pipeline with no continuation it is the end-of-statement comment.
        // It is resolved against capture point C below (they are mutually exclusive).
        string? sourceTrailing = TryConsumeComment();

        // Multi-line pipeline continuation: if the next line starts with Indent + Pipe,
        // treat it as a continuation of the current pipeline. This allows pipelines to
        // span multiple lines (each segment on its own line), which is a prerequisite
        // for per-segment comment preservation (Phase B).
        while (Current.Kind == KsTokenKind.Indent && Peek(1).Kind == KsTokenKind.Pipe)
        {
            Advance(); // consume Indent
            Advance(); // consume Pipe
            if (IsKeyword("forEach"))
            {
                Error("KS061", "'forEach' is not valid as a pipeline segment. Use prefix form: 'forEach <source> as <item>'");
                break;
            }
            var seg = ParseSegment();
            // Capture point B — a trailing comment on this continuation segment's line
            // (`> Func // cmt`). This is the per-segment comment that forces multi-line
            // rendering and maps to the segment's BP node.
            seg.Comment = TryConsumeComment();
            segments.Add(seg);
        }

        // Terminal assignment `= name` becomes a variable-tap segment.
        if (Match(KsTokenKind.Assign))
        {
            if (Current.Kind != KsTokenKind.Identifier)
                Error("KS040", "Expected variable name after '='");
            else
            {
                var nameTok = Advance();
                segments.Add(new KsPipelineSegment
                {
                    Target = nameTok.Text,
                    IsVariableTap = true,
                    SourceLine = nameTok.Line,
                    SourceText = nameTok.Text,
                });
            }
        }
        Match(KsTokenKind.Semicolon);

        // Capture point C — end-of-statement trailing comment for the single-line form
        // (after the last token on the one physical line). Mutually exclusive with A:
        // when a multi-line continuation ran, the last segment's comment was captured at
        // point B and the current token is a new-line Indent (not a Comment).
        string? endTrailing = TryConsumeComment();

        return new KsPipeline
        {
            Sources = sources.ToImmutable(),
            Segments = segments.ToImmutable(),
            SourceLine = sources.Count > 0 ? sources[0].SourceLine : Current.Line,
            TrailingComment = sourceTrailing ?? endTrailing,
        };
    }

    private KsPipelineSegment ParseSegment()
    {
        // A segment is either:
        //   name '(' funcArgs ')'  — a call (args must be literals/placeholders per v6.0)
        //   name                    — a variable tap
        if (Current.Kind != KsTokenKind.Identifier)
        {
            Error("KS041", "Expected segment name after '>'");
            return new KsPipelineSegment { Target = "?", SourceLine = Current.Line };
        }
        var nameTok = Advance();
        var args = ImmutableArray.CreateBuilder<KsNode>();
        var rawArgs = ImmutableArray.CreateBuilder<string>();
        bool isCall = false;

        if (Match(KsTokenKind.LParen))
        {
            isCall = true;
            if (Current.Kind != KsTokenKind.RParen)
            {
                args.Add(ParseLiteralOrPlaceholder());
                rawArgs.Add(args[^1].SourceText);
                while (Match(KsTokenKind.Comma))
                {
                    args.Add(ParseLiteralOrPlaceholder());
                    rawArgs.Add(args[^1].SourceText);
                }
            }
            if (!Match(KsTokenKind.RParen))
                Error("KS042", "Expected ')' to close call arguments");
        }

        var rawArgsArray = rawArgs.ToImmutable();
        return new KsPipelineSegment
        {
            Target = nameTok.Text,
            Args = args.ToImmutable(),
            RawArgs = rawArgsArray,
            IsVariableTap = !isCall && args.Count == 0 && false,
            // ^ never mark a `> name` as a tap here — only `= name` becomes a tap.
            // A bare `> name` is a call with no args (the pipeline value is the
            // implicit single arg via `_`). The renderer/codegen handle this.
            SourceLine = nameTok.Line,
            SourceText = isCall
                ? $"{nameTok.Text}({string.Join(", ", rawArgsArray)})"
                : nameTok.Text,
        };
    }

    // ── Expressions ──

    /// <summary>
    /// Parses a function argument inside parentheses. Per the v6.0 syntax rule,
    /// only literals and '_' placeholders are allowed inside function parens;
    /// non-literal values MUST flow through pipeline sources. Use
    /// <see cref="ParseExpression"/> for pipeline sources and conditions where
    /// identifiers are valid.
    /// </summary>
    private KsNode ParseLiteralOrPlaceholder()
    {
        switch (Current.Kind)
        {
            case KsTokenKind.StringLiteral:
                { var t = Advance(); return new KsLiteral { Kind = KsLiteralKind.String, Value = t.Value, SourceText = $"\"{t.Value}\"", SourceLine = t.Line }; }
            case KsTokenKind.IntegerLiteral:
                { var t = Advance(); return new KsLiteral { Kind = KsLiteralKind.Integer, Value = t.Value, SourceText = t.Text, SourceLine = t.Line }; }
            case KsTokenKind.DoubleLiteral:
                { var t = Advance(); return new KsLiteral { Kind = KsLiteralKind.Double, Value = t.Value, SourceText = t.Text, SourceLine = t.Line }; }
            case KsTokenKind.CharLiteral:
                { var t = Advance(); return new KsLiteral { Kind = KsLiteralKind.Char, Value = t.Value, SourceText = $"'{t.Value}'", SourceLine = t.Line }; }
            case KsTokenKind.BooleanLiteral:
                { var t = Advance(); return new KsLiteral { Kind = KsLiteralKind.Boolean, Value = t.Value, SourceText = t.Text, SourceLine = t.Line }; }
            case KsTokenKind.NullLiteral:
                { var t = Advance(); return new KsLiteral { Kind = KsLiteralKind.Null, Value = null, SourceText = "null", SourceLine = t.Line }; }
            case KsTokenKind.Placeholder:
                { var t = Advance(); return new KsPlaceholder { Index = 0, SourceText = "_", SourceLine = t.Line }; }
            default:
                Error("KS051", $"Function arguments may only be literals or '_' placeholders (v6.0 rule); got: {Current.Kind} '{Current.Text}'. Use pipeline form: 'value > Func(...)'");
                Advance();
                return new KsLiteral { Kind = KsLiteralKind.Null, Value = null, SourceText = "null", SourceLine = Current.Line };
        }
    }

    /// <summary>
    /// Parses a general expression: literal, placeholder, identifier, or a call
    /// whose arguments are literals/placeholders only (v6.0 rule). Used for
    /// pipeline sources, forEach sources, and switch selectors where identifiers
    /// are valid.
    /// </summary>
    private KsNode ParseExpression()
    {
        switch (Current.Kind)
        {
            case KsTokenKind.StringLiteral:
            case KsTokenKind.IntegerLiteral:
            case KsTokenKind.DoubleLiteral:
            case KsTokenKind.CharLiteral:
            case KsTokenKind.BooleanLiteral:
            case KsTokenKind.NullLiteral:
            case KsTokenKind.Placeholder:
                return ParseLiteralOrPlaceholder();
            case KsTokenKind.Identifier:
                {
                    var t = Advance();
                    // `name(funcArg*)` — a call as a primary expression (e.g. Range(0, 10, 1)).
                    // Per v6.0 rule, call args may only be literals/placeholders.
                    if (Current.Kind == KsTokenKind.LParen)
                    {
                        Advance();  // consume '('
                        var args = ImmutableArray.CreateBuilder<KsNode>();
                        var rawArgs = ImmutableArray.CreateBuilder<string>();
                        if (Current.Kind != KsTokenKind.RParen)
                        {
                            args.Add(ParseLiteralOrPlaceholder());
                            rawArgs.Add(args[^1].SourceText);
                            while (Match(KsTokenKind.Comma))
                            {
                                args.Add(ParseLiteralOrPlaceholder());
                                rawArgs.Add(args[^1].SourceText);
                            }
                        }
                        if (!Match(KsTokenKind.RParen))
                            Error("KS052", "Expected ')' to close call arguments");
                        var rawArgsArray = rawArgs.ToImmutable();
                        return new KsCall
                        {
                            MethodName = t.Text,
                            FullMethodName = t.Text,
                            Args = args.ToImmutable(),
                            RawArgs = rawArgsArray,
                            SourceText = $"{t.Text}({string.Join(", ", rawArgsArray)})",
                            SourceLine = t.Line,
                        };
                    }
                    return new KsIdentifier { Name = t.Text, SourceText = t.Text, SourceLine = t.Line };
                }
            default:
                Error("KS050", $"Unexpected token in expression: {Current.Kind} '{Current.Text}'");
                Advance();
                return new KsIdentifier { Name = "?", SourceText = "?", SourceLine = Current.Line };
        }
    }

    /// <summary>
    /// Parses a control-flow header pipeline expression: a simple expression, or a
    /// multi-source pipeline (<c>src1, src2 &gt; Func(args) &gt; ...</c>), optionally
    /// spanning multiple lines (each continuation segment on its own indented <c>&gt;</c>
    /// line). Returns the expression (KsNode or KsPipeline) plus the last segment's
    /// inline comment captured during continuation (usually null — the last segment's
    /// comment is captured post-colon by the caller, since the ':' terminator sits on
    /// the last segment's line before any inline comment).
    /// </summary>
    private (KsNode Expr, string? LastSegComment) ParseHeaderPipelineExpression()
    {
        var firstSource = ParseExpression();

        // Simple condition: no comma, no pipe → return the expression directly.
        if (Current.Kind != KsTokenKind.Comma && Current.Kind != KsTokenKind.Pipe)
            return (firstSource, null);

        // Pipeline condition: build sources + segments.
        var sources = ImmutableArray.CreateBuilder<KsNode>();
        sources.Add(firstSource);
        while (Match(KsTokenKind.Comma))
            sources.Add(ParseExpression());

        var segments = ImmutableArray.CreateBuilder<KsPipelineSegment>();
        string? lastSegComment = null;
        while (Match(KsTokenKind.Pipe))
        {
            if (IsKeyword("forEach"))
            {
                Error("KS061", "'forEach' is not valid in a pipeline expression. Use prefix form: 'forEach <source> as <item>'");
                break;
            }
            segments.Add(ParseSegment());
        }

        // Multi-line header continuation: lines at the body indent starting with '>'
        // are additional segments. Each may carry an inline comment (intermediate
        // segments). The last segment's comment is typically captured post-colon by
        // the caller (the ':' sits on the last segment's line before the inline comment).
        while (Current.Kind == KsTokenKind.Indent && Peek(1).Kind == KsTokenKind.Pipe)
        {
            Advance();  // consume Indent
            Advance();  // consume Pipe
            if (IsKeyword("forEach"))
            {
                Error("KS061", "'forEach' is not valid in a pipeline expression. Use prefix form: 'forEach <source> as <item>'");
                break;
            }
            var seg = ParseSegment();
            var segComment = TryConsumeComment();
            seg.Comment = segComment;
            lastSegComment = segComment;
            segments.Add(seg);
        }

        if (segments.Count == 0)
            Error("KS060", "Multiple sources in condition require a '>' pipeline segment");

        return (new KsPipeline
        {
            Sources = sources.ToImmutable(),
            Segments = segments.ToImmutable(),
            SourceLine = sources[0].SourceLine,
        }, lastSegComment);
    }

    // ── Body parsing ──

    /// <summary>
    /// The indent level of the last Indent token we consumed. Used to compute the
    /// expected body indent (keyword indent + 1) and to detect where a body ends
    /// (when a line returns to keyword indent or lower).
    /// </summary>
    private int LastConsumedIndentLevel()
    {
        // Walk backwards through consumed tokens to find the most recent Indent.
        for (int i = _pos - 1; i >= 0; i--)
        {
            if (_tokens[i].Kind == KsTokenKind.Indent)
                return _tokens[i].IndentLevel;
        }
        return 0;
    }

    /// <summary>
    /// Parses a body of statements at indent level <paramref name="bodyIndent"/>. Stops
    /// when it encounters a line at a lower indent (the body has ended) or a higher
    /// indent that's not equal to bodyIndent (reports an error and skips).
    /// </summary>
    private ImmutableArray<KsStatement> ParseBody(int bodyIndent, string context)
    {
        var body = ImmutableArray.CreateBuilder<KsStatement>();
        List<string>? pendingLeading = null;  // accumulated full-line comments for the next statement
        while (Current.Kind == KsTokenKind.Indent && Current.IndentLevel == bodyIndent)
        {
            Advance();  // consume Indent(bodyIndent)
            // A full-line comment emits Indent(bodyIndent) + Comment. Accumulate it as a
            // leading comment for the next statement in this body.
            if (Current.Kind == KsTokenKind.Comment)
            {
                (pendingLeading ??= new()).Add(Current.Text);
                Advance();
                continue;
            }
            // If the next token is `else` at bodyIndent, it belongs to the enclosing
            // if — back out so ParseIf can see it.
            if (IsKeyword("else"))
            {
                _pos--;  // unconsume the Indent so the if's else detection works
                break;
            }
            var stmt = ParseStatement();
            if (pendingLeading is { Count: > 0 })
            {
                stmt.LeadingComment = string.Join("\n", pendingLeading);
                pendingLeading = null;
            }
            body.Add(stmt);
        }
        if (body.Count == 0)
            Error("KS062", $"{context} body is empty");
        return body.ToImmutable();
    }
}