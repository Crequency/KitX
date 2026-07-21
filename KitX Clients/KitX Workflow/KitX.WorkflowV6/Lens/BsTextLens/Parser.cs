namespace KitX.WorkflowV6.Lens.BsTextLens;

using KitX.WorkflowV6.Ir.Ast;

// ─────────────────────────────────────────────────────────────────────────────
// Parser — recursive-descent parser for the v6 indented BS grammar.
//
// Replaces the v5 Superpower token-combinator parser. Indented grammars (Python
// style) don't compose well with token combinators — the combinator library wants
// to look-ahead by tokens, but indent/dedent are *line-level* events. A hand-rolled
// recursive-descent parser with an indent stack is the standard solution and is
// what the implementation plan §Phase 2 prescribes.
//
// Grammar (informal — full grammar in BlockScriptGrammarRule.md v6.0):
//
//   program        ::= declBlock* statement*
//   declBlock      ::= ('const' | 'var') '{' declRow* '}'
//   declRow        ::= type name ('=' expr)?    // on one line
//   statement      ::= ifStmt | switchStmt | forEachStmt | whileStmt
//                    | break | continue | exit ('(' ')')?
//                    | pipeline
//   ifStmt         ::= 'if' expr INDENT statement+ DEDENT
//                      ('else' (ifStmt | INDENT statement+ DEDENT))?
//   switchStmt     ::= 'switch' expr INDENT arm+ DEDENT
//   arm            ::= (integer | 'default') ':' statement+ (inline or block)
//   forEachStmt    ::= 'forEach' expr 'as' name INDENT statement+ DEDENT
//   whileStmt      ::= 'while' expr INDENT statement+ DEDENT
//   pipeline       ::= expr (',' expr)* ('>' call)* ('=' name)? ';'?
//   call           ::= name '(' (expr (',' expr)*)? ')' | name
//   expr           ::= literal | identifier | call
//   literal        ::= string | integer | double | char | true | false | null | '_'
//
// INDENT/DEDENT are not real tokens — the parser tracks the indent level of the
// current line and treats a higher level as "enter body", a lower level as "exit
// body". parseBody(currentLevel) consumes statements while their indent is
// greater than currentLevel.
//
// The parser is recursive descent with no backtracking (each lookahead token
// unambiguously picks a rule). Errors are collected into the DiagnosticSink and
// the parser recovers as best it can.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Recursive-descent parser for the v6 indented BS grammar. Produces a
/// <see cref="BsProgram"/> AST. Pure: the same tokens always yield the same AST.
/// </summary>
internal sealed class Parser
{
    private readonly List<BsToken> _tokens;
    private readonly DiagnosticSink _sink;
    private int _pos;

    private Parser(List<BsToken> tokens, DiagnosticSink sink)
    {
        _tokens = tokens;
        _sink = sink;
        _pos = 0;
    }

    /// <summary>Parses a token list into a <see cref="BsProgram"/> AST.</summary>
    public static (BsProgram Program, DiagnosticSink Diagnostics) Parse(List<BsToken> tokens, DiagnosticSink sink)
    {
        var parser = new Parser(tokens, sink);
        var program = parser.ParseProgram();
        return (program, parser._sink);
    }

    // ── Token helpers ──

    private BsToken Current => _tokens[_pos];
    private BsToken Peek(int offset = 0) =>
        _pos + offset < _tokens.Count ? _tokens[_pos + offset] : _tokens[^1];

    private bool AtEnd => Current.Kind == BsTokenKind.EndOfInput;

    private BsToken Advance()
    {
        var t = Current;
        if (!AtEnd) _pos++;
        return t;
    }

    private bool Match(BsTokenKind kind)
    {
        if (Current.Kind == kind) { Advance(); return true; }
        return false;
    }

    private bool IsKeyword(string word) =>
        Current.Kind == BsTokenKind.Identifier && Current.Text == word;

    private bool MatchKeyword(string word)
    {
        if (IsKeyword(word)) { Advance(); return true; }
        return false;
    }

    private void Error(string code, string message, BsToken? at = null)
    {
        var t = at ?? Current;
        _sink.AddError(code, message, t.Line, t.Column);
    }

    // ── Program ──

    private BsProgram ParseProgram()
    {
        var body = ImmutableArray.CreateBuilder<BsStatement>();
        BsConstBlock? constBlock = null;
        BsVarBlock? varBlock = null;

        while (!AtEnd)
        {
            // Find the next Indent token at level 0.
            if (Current.Kind != BsTokenKind.Indent) { Advance(); continue; }
            var indent = Current.IndentLevel;
            if (indent != 0)
            {
                Error("BS010", $"Top-level statement must be at indent 0 (got {indent})");
                while (!AtEnd && Current.Kind != BsTokenKind.Indent) Advance();
                continue;
            }
            Advance();  // consume Indent(0)

            if (MatchKeyword("const"))
            {
                if (constBlock is not null)
                    Error("BS011", "Duplicate const block");
                constBlock = ParseConstBlock();
                continue;
            }
            if (MatchKeyword("var"))
            {
                if (varBlock is not null)
                    Error("BS011", "Duplicate var block");
                varBlock = ParseVarBlock();
                continue;
            }
            body.Add(ParseStatement());
        }

        return new BsProgram
        {
            ConstBlock = constBlock,
            VarBlock = varBlock,
            Body = body.ToImmutable(),
            SourceLine = 1,
        };
    }

    // ── Decl blocks ──

    private BsConstBlock ParseConstBlock()
    {
        var decls = ImmutableArray.CreateBuilder<BsConstDecl>();
        ExpectLBrace();
        while (!AtEnd && Current.Kind != BsTokenKind.RBrace)
        {
            // Each decl row lives on its own line, prefixed by an Indent token.
            if (Current.Kind == BsTokenKind.Indent) Advance();
            if (Current.Kind == BsTokenKind.RBrace) break;
            decls.Add(ParseConstRow());
            // Skip any remaining tokens on this line.
            while (!AtEnd && Current.Kind != BsTokenKind.Indent
                          && Current.Kind != BsTokenKind.RBrace) Advance();
        }
        Match(BsTokenKind.RBrace);
        return new BsConstBlock { Declarations = decls.ToImmutable() };
    }

    private BsVarBlock ParseVarBlock()
    {
        var decls = ImmutableArray.CreateBuilder<BsVarDecl>();
        ExpectLBrace();
        while (!AtEnd && Current.Kind != BsTokenKind.RBrace)
        {
            if (Current.Kind == BsTokenKind.Indent) Advance();
            if (Current.Kind == BsTokenKind.RBrace) break;
            decls.Add(ParseVarRow());
            while (!AtEnd && Current.Kind != BsTokenKind.Indent
                          && Current.Kind != BsTokenKind.RBrace) Advance();
        }
        Match(BsTokenKind.RBrace);
        return new BsVarBlock { Declarations = decls.ToImmutable() };
    }

    private BsConstDecl ParseConstRow()
    {
        var (typeTok, nameTok, initExpr, src) = ParseDeclRowCore();
        return new BsConstDecl
        {
            Name = nameTok.Text,
            Type = typeTok.Text,
            InitialValueExpression = initExpr,
            SourceText = src,
            SourceLine = typeTok.Line,
        };
    }

    private BsVarDecl ParseVarRow()
    {
        var (typeTok, nameTok, initExpr, src) = ParseDeclRowCore();
        return new BsVarDecl
        {
            Name = nameTok.Text,
            Type = typeTok.Text,
            InitialValueExpression = initExpr,
            SourceText = src,
            SourceLine = typeTok.Line,
        };
    }

    private (BsToken typeTok, BsToken nameTok, string? initExpr, string src) ParseDeclRowCore()
    {
        // Form: <type> <name> ['=' <expr-text>]
        var typeTok = Current.Kind == BsTokenKind.Identifier ? Advance() : Current;
        var nameTok = Current.Kind == BsTokenKind.Identifier ? Advance() : Current;
        if (typeTok.Kind != BsTokenKind.Identifier)
            Error("BS012", "Declaration must start with a type name", typeTok);
        if (nameTok.Kind != BsTokenKind.Identifier)
            Error("BS012", "Declaration must have a name after the type", nameTok);

        string? initExpr = null;
        if (Match(BsTokenKind.Assign))
        {
            // Capture the rest of the line as the initialiser expression text.
            int start = _pos;
            while (!AtEnd && Current.Kind != BsTokenKind.Indent
                          && Current.Kind != BsTokenKind.RBrace) Advance();
            initExpr = ReconstructText(_tokens, start, _pos).Trim();
        }

        var src = $"{typeTok.Text} {nameTok.Text}{(initExpr is null ? "" : " = " + initExpr)}";
        return (typeTok, nameTok, initExpr, src);
    }

    private void ExpectLBrace()
    {
        if (!Match(BsTokenKind.LBrace))
            Error("BS013", "Expected '{' after const/var");
    }

    private static string ReconstructText(List<BsToken> tokens, int from, int toExclusive)
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

    private BsStatement ParseStatement()
    {
        // Current is the first token of the statement (the Indent was consumed).
        switch (Current.Kind)
        {
            case BsTokenKind.Identifier:
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

    private BsIf ParseIf()
    {
        var ifTok = Advance();  // 'if'
        var cond = ParseExpression();
        int keywordIndent = LastConsumedIndentLevel();
        var thenBody = ParseBody(keywordIndent + 1, $"if on line {ifTok.Line}");
        ImmutableArray<BsStatement> elseBody = [];

        // `else` should sit at the same indent level as the `if`. After ParseBody
        // returns, the current token should be an Indent at keywordIndent (because
        // ParseBody stops when it sees a lower indent). Check whether that Indent
        // is followed by the `else` keyword.
        if (Current.Kind == BsTokenKind.Indent && Current.IndentLevel == keywordIndent)
        {
            // Peek one token ahead: is it `else`?
            if (Peek(1).Kind == BsTokenKind.Identifier && Peek(1).Text == "else")
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
                    elseBody = ParseBody(keywordIndent + 1, $"else on line {elseTok.Line}");
                }
            }
        }

        return new BsIf
        {
            Condition = cond,
            ThenBody = thenBody,
            ElseBody = elseBody,
            SourceLine = ifTok.Line,
        };
    }

    private BsSwitch ParseSwitch()
    {
        var swTok = Advance();  // 'switch'
        var selector = ParseExpression();
        int keywordIndent = LastConsumedIndentLevel();
        int armIndent = keywordIndent + 1;
        var arms = ImmutableArray.CreateBuilder<ImmutableArray<BsStatement>>();
        ImmutableArray<BsStatement> defaultBody = [];
        bool sawDefault = false;

        while (Current.Kind == BsTokenKind.Indent && Current.IndentLevel == armIndent)
        {
            Advance();  // consume Indent(armIndent)
            if (MatchKeyword("default"))
            {
                if (sawDefault) Error("BS022", "Duplicate default arm");
                sawDefault = true;
                if (!Match(BsTokenKind.Colon))
                    Error("BS020", "Expected ':' after 'default'");
                defaultBody = ParseArmBody(armIndent);
            }
            else if (Current.Kind == BsTokenKind.IntegerLiteral)
            {
                Advance();
                if (!Match(BsTokenKind.Colon))
                    Error("BS020", "Expected ':' after case label");
                arms.Add(ParseArmBody(armIndent));
            }
            else
            {
                Error("BS021", "Expected case label or 'default' in switch arm");
                while (!AtEnd && Current.Kind != BsTokenKind.Indent) Advance();
            }
        }

        return new BsSwitch
        {
            Selector = selector,
            Arms = arms.ToImmutable(),
            Default = defaultBody,
            SourceLine = swTok.Line,
        };
    }

    private ImmutableArray<BsStatement> ParseArmBody(int armIndent)
    {
        // An arm body is either:
        //   (a) inline — more tokens follow the ':' on the same line
        //   (b) a block — statements at armIndent + 1
        if (Current.Kind != BsTokenKind.Indent && Current.Kind != BsTokenKind.EndOfInput)
        {
            // Inline: parse the rest of the line as one statement.
            return [ParseStatement()];
        }
        // Block at armIndent + 1.
        return ParseBody(armIndent + 1, "switch arm");
    }

    private BsForEach ParseForEach()
    {
        var feTok = Advance();  // 'forEach'
        var source = ParseExpression();
        if (!MatchKeyword("as"))
            Error("BS030", "Expected 'as' after forEach source");
        if (Current.Kind != BsTokenKind.Identifier)
            Error("BS031", "Expected item name after 'as'");
        var itemName = Advance().Text;
        int keywordIndent = LastConsumedIndentLevel();
        var body = ParseBody(keywordIndent + 1, $"forEach on line {feTok.Line}");
        return new BsForEach
        {
            Source = source,
            ItemName = itemName,
            Body = body,
            SourceLine = feTok.Line,
        };
    }

    private BsWhile ParseWhile()
    {
        var whTok = Advance();  // 'while'
        var cond = ParseExpression();
        int keywordIndent = LastConsumedIndentLevel();
        var body = ParseBody(keywordIndent + 1, $"while on line {whTok.Line}");
        return new BsWhile
        {
            Condition = cond,
            Body = body,
            SourceLine = whTok.Line,
        };
    }

    private BsBreak ParseBreak()
    {
        var t = Advance();
        Match(BsTokenKind.Semicolon);
        return new BsBreak { SourceLine = t.Line, SourceText = "break" };
    }

    private BsContinue ParseContinue()
    {
        var t = Advance();
        Match(BsTokenKind.Semicolon);
        return new BsContinue { SourceLine = t.Line, SourceText = "continue" };
    }

    private BsExit ParseExit()
    {
        var t = Advance();
        // Tolerate `exit()` — consume the parens if present.
        if (Match(BsTokenKind.LParen)) Match(BsTokenKind.RParen);
        Match(BsTokenKind.Semicolon);
        return new BsExit { SourceLine = t.Line, SourceText = "exit" };
    }

    // ── Pipelines and expressions ──

    private BsStatement ParsePipelineOrAssignment()
    {
        // A pipeline line: <src> (',' <src>)* ('>' <segment>)* ('=' <name>)? ';'?
        // A bare call:    <call>   (lowered to a one-source pipeline with one call segment)
        //
        // Special case: when a segment target is the keyword `forEach`, the pipeline
        // is actually a forEach statement — `source > forEach as item` desugars to
        // `forEach source as item`. This is the form used in the discussion-notes
        // §4.3 example (Range(0, loopMax, 1) > forEach as i). Both forms produce the
        // same ForEachStatement; the §3.3 #4 form `forEach list as item` is the
        // canonical one, and the pipeline form is sugar.
        var sources = ImmutableArray.CreateBuilder<BsNode>();
        sources.Add(ParseExpression());
        while (Match(BsTokenKind.Comma))
            sources.Add(ParseExpression());

        var segments = ImmutableArray.CreateBuilder<BsPipelineSegment>();
        while (Match(BsTokenKind.Pipe))
        {
            // Intercept `forEach` as a pipeline segment: desugar to ForEachStatement.
            if (IsKeyword("forEach"))
            {
                Advance();  // consume 'forEach'
                if (!MatchKeyword("as"))
                    Error("BS030", "Expected 'as' after forEach");
                if (Current.Kind != BsTokenKind.Identifier)
                    Error("BS031", "Expected item name after 'as'");
                else
                {
                    var itemName = Advance().Text;
                    int keywordIndent = LastConsumedIndentLevel();
                    var body = ParseBody(keywordIndent + 1, "forEach");
                    return new BsForEach
                    {
                        Source = sources.Count == 1 ? sources[0]
                              : new BsPipeline { Sources = sources.ToImmutable(), Segments = [], SourceLine = sources[0].SourceLine },
                        ItemName = itemName,
                        Body = body,
                        SourceLine = sources[0].SourceLine,
                    };
                }
            }
            segments.Add(ParseSegment());
        }

        // Terminal assignment `= name` becomes a variable-tap segment.
        if (Match(BsTokenKind.Assign))
        {
            if (Current.Kind != BsTokenKind.Identifier)
                Error("BS040", "Expected variable name after '='");
            else
            {
                var nameTok = Advance();
                segments.Add(new BsPipelineSegment
                {
                    Target = nameTok.Text,
                    IsVariableTap = true,
                    SourceLine = nameTok.Line,
                    SourceText = nameTok.Text,
                });
            }
        }
        Match(BsTokenKind.Semicolon);

        return new BsPipeline
        {
            Sources = sources.ToImmutable(),
            Segments = segments.ToImmutable(),
            SourceLine = sources.Count > 0 ? sources[0].SourceLine : Current.Line,
        };
    }

    private BsPipelineSegment ParseSegment()
    {
        // A segment is either:
        //   name '(' args ')'  — a call (args may include `_` placeholders)
        //   name                — a variable tap
        if (Current.Kind != BsTokenKind.Identifier)
        {
            Error("BS041", "Expected segment name after '>'");
            return new BsPipelineSegment { Target = "?", SourceLine = Current.Line };
        }
        var nameTok = Advance();
        var args = ImmutableArray.CreateBuilder<BsNode>();
        var rawArgs = ImmutableArray.CreateBuilder<string>();
        bool isCall = false;

        if (Match(BsTokenKind.LParen))
        {
            isCall = true;
            if (Current.Kind != BsTokenKind.RParen)
            {
                args.Add(ParseExpression());
                rawArgs.Add(args[^1].SourceText);
                while (Match(BsTokenKind.Comma))
                {
                    args.Add(ParseExpression());
                    rawArgs.Add(args[^1].SourceText);
                }
            }
            if (!Match(BsTokenKind.RParen))
                Error("BS042", "Expected ')' to close call arguments");
        }

        return new BsPipelineSegment
        {
            Target = nameTok.Text,
            Args = args.ToImmutable(),
            RawArgs = rawArgs.ToImmutable(),
            IsVariableTap = !isCall && args.Count == 0 && false,
            // ^ never mark a `> name` as a tap here — only `= name` becomes a tap.
            // A bare `> name` is a call with no args (the pipeline value is the
            // implicit single arg via `_`). The renderer/codegen handle this.
            SourceLine = nameTok.Line,
            SourceText = nameTok.Text,
        };
    }

    // ── Expressions ──

    private BsNode ParseExpression()
    {
        switch (Current.Kind)
        {
            case BsTokenKind.StringLiteral:
                {
                    var t = Advance();
                    return new BsLiteral { Kind = BsLiteralKind.String, Value = t.Value, SourceText = $"\"{t.Value}\"", SourceLine = t.Line };
                }
            case BsTokenKind.IntegerLiteral:
                {
                    var t = Advance();
                    return new BsLiteral { Kind = BsLiteralKind.Integer, Value = t.Value, SourceText = t.Text, SourceLine = t.Line };
                }
            case BsTokenKind.DoubleLiteral:
                {
                    var t = Advance();
                    return new BsLiteral { Kind = BsLiteralKind.Double, Value = t.Value, SourceText = t.Text, SourceLine = t.Line };
                }
            case BsTokenKind.CharLiteral:
                {
                    var t = Advance();
                    return new BsLiteral { Kind = BsLiteralKind.Char, Value = t.Value, SourceText = $"'{t.Value}'", SourceLine = t.Line };
                }
            case BsTokenKind.BooleanLiteral:
                {
                    var t = Advance();
                    return new BsLiteral { Kind = BsLiteralKind.Boolean, Value = t.Value, SourceText = t.Text, SourceLine = t.Line };
                }
            case BsTokenKind.NullLiteral:
                {
                    var t = Advance();
                    return new BsLiteral { Kind = BsLiteralKind.Null, Value = null, SourceText = "null", SourceLine = t.Line };
                }
            case BsTokenKind.Placeholder:
                {
                    var t = Advance();
                    return new BsPlaceholder { Index = 0, SourceText = "_", SourceLine = t.Line };
                }
            case BsTokenKind.Identifier:
                {
                    var t = Advance();
                    // `name(args)` — a call as a primary expression (e.g. Range(0, 10, 1)).
                    if (Current.Kind == BsTokenKind.LParen)
                    {
                        Advance();  // consume '('
                        var args = ImmutableArray.CreateBuilder<BsNode>();
                        var rawArgs = ImmutableArray.CreateBuilder<string>();
                        if (Current.Kind != BsTokenKind.RParen)
                        {
                            args.Add(ParseExpression());
                            rawArgs.Add(args[^1].SourceText);
                            while (Match(BsTokenKind.Comma))
                            {
                                args.Add(ParseExpression());
                                rawArgs.Add(args[^1].SourceText);
                            }
                        }
                        if (!Match(BsTokenKind.RParen))
                            Error("BS052", "Expected ')' to close call arguments");
                        var rawArgsArray = rawArgs.ToImmutable();
                        return new BsCall
                        {
                            MethodName = t.Text,
                            FullMethodName = t.Text,
                            Args = args.ToImmutable(),
                            RawArgs = rawArgsArray,
                            SourceText = $"{t.Text}({string.Join(", ", rawArgsArray)})",
                            SourceLine = t.Line,
                        };
                    }
                    return new BsIdentifier { Name = t.Text, SourceText = t.Text, SourceLine = t.Line };
                }
            default:
                Error("BS050", $"Unexpected token in expression: {Current.Kind} '{Current.Text}'");
                Advance();
                return new BsIdentifier { Name = "?", SourceText = "?", SourceLine = Current.Line };
        }
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
            if (_tokens[i].Kind == BsTokenKind.Indent)
                return _tokens[i].IndentLevel;
        }
        return 0;
    }

    /// <summary>
    /// Parses a body of statements at indent level <paramref name="bodyIndent"/>. Stops
    /// when it encounters a line at a lower indent (the body has ended) or a higher
    /// indent that's not equal to bodyIndent (reports an error and skips).
    /// </summary>
    private ImmutableArray<BsStatement> ParseBody(int bodyIndent, string context)
    {
        var body = ImmutableArray.CreateBuilder<BsStatement>();
        while (Current.Kind == BsTokenKind.Indent && Current.IndentLevel == bodyIndent)
        {
            Advance();  // consume Indent(bodyIndent)
            // If the next token is `else` at bodyIndent, it belongs to the enclosing
            // if — back out so ParseIf can see it.
            if (IsKeyword("else"))
            {
                _pos--;  // unconsume the Indent so the if's else detection works
                break;
            }
            body.Add(ParseStatement());
        }
        if (body.Count == 0)
            Error("BS062", $"{context} body is empty");
        return body.ToImmutable();
    }
}