namespace KitX.WorkflowV6.Lens.KsTextLens;

using KitX.WorkflowV6.Ir;
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
//                    | break | continue
//                    | pipeline
//   ifStmt         ::= 'if' condition INDENT statement+ DEDENT
//                      ('else' ':' INDENT statement+ DEDENT)?   // no `else if` (KS064)
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
    /// <summary>
    /// Maximum expression/statement nesting depth (B5c). Recursive-descent parsing
    /// (ParseExpression paren nesting, ParseBody statement nesting) grows the call
    /// stack linearly with input nesting; a malicious/extreme input could otherwise
    /// raise an uncatchable StackOverflowException. Crossed depth aborts parsing via
    /// <see cref="NestingLimitExceededException"/> (caught in <see cref="ParseProgram"/>)
    /// and reports KS078.
    /// </summary>
    private const int MaxNestingDepth = 200;

    private readonly List<KsToken> _tokens;
    private readonly KsDiagnosticSink _sink;
    private int _pos;
    private int _nestingDepth;

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

    private bool RejectForEachInPipeline(string context)
    {
        if (IsKeyword("forEach"))
        {
            Error(KsErrors.ForEachInPipeline, $"'forEach' is not valid {context}. Use prefix form: 'forEach <source> as <item>'");
            return true;
        }
        return false;
    }

    /// <summary>
    /// True when the current Indent+Comment line is shortly followed by an Indent+Pipe
    /// continuation line — i.e. a full-line comment wedged between multi-line pipeline
    /// continuations (rejected with KS065). Lookahead is bounded: a comment line is at
    /// most a few tokens from the continuation it interrupts.
    /// </summary>
    private bool HasContinuationAfterCommentLine()
    {
        for (int j = 2; j <= 8; j++)
            if (Peek(j).Kind == KsTokenKind.Indent && Peek(j + 1).Kind == KsTokenKind.Pipe)
                return true;
        return false;
    }

    private sealed class CommentAccumulator
    {
        private List<string>? _pending;

        public void Add(string text) => (_pending ??= new()).Add(text);

        public string? Detach()
        {
            if (_pending is null or { Count: 0 }) return null;
            var joined = string.Join("\n", _pending);
            _pending.Clear();
            return joined;
        }
    }

    /// <summary>
    /// Enters one recursion level of expression/statement nesting (B5c). Throws
    /// <see cref="NestingLimitExceededException"/> when <see cref="MaxNestingDepth"/>
    /// is crossed — the exception unwinds to <see cref="ParseProgram"/>, which records
    /// KS078 and aborts parsing, instead of letting the recursion overflow the stack.
    /// </summary>
    private void EnterNesting()
    {
        _nestingDepth++;
        if (_nestingDepth > MaxNestingDepth)
            throw new NestingLimitExceededException();
    }

    /// <summary>Private abort signal for the nesting-depth guard (never leaks outside Parser.Parse).</summary>
    private sealed class NestingLimitExceededException : Exception { }

    // ── Program ──

    private KsProgram ParseProgram()
    {
        var body = ImmutableArray.CreateBuilder<KsStatement>();
        KsConstBlock? constBlock = null;
        KsVarBlock? varBlock = null;
        var commentAcc = new CommentAccumulator();

        try
        {
            while (!AtEnd)
            {
                // Find the next Indent token at level 0.
                if (Current.Kind != KsTokenKind.Indent) { Advance(); continue; }
                var indent = Current.IndentLevel;
                if (indent != 0)
                {
                    Error(KsErrors.TopLevelStatementIndent, $"Top-level statement must be at indent 0 (got {indent})");
                    Advance();  // consume the wrong-level Indent to avoid infinite loop
                    while (!AtEnd && Current.Kind != KsTokenKind.Indent) Advance();
                    continue;
                }
                Advance();  // consume Indent(0)

                // A full-line comment emits Indent(0) + Comment. Accumulate it as a leading
                // comment for the next top-level statement.
                if (Current.Kind == KsTokenKind.Comment)
                {
                    commentAcc.Add(Current.Text);
                    Advance();
                    continue;
                }

                // Comments directly above a const/var block become the block's doc comment
                // (KS-side privileged — never projected to the BP graph). The accumulation
                // is consumed here so it can never leak onto a following statement; comments
                // between the const block and the var block go to the var block.
                if (MatchKeyword("const"))
                {
                    var doc = commentAcc.Detach();
                    if (constBlock is not null)
                        Error(KsErrors.DuplicateDeclBlock, "Duplicate const block");
                    var block = ParseConstBlock();
                    // Block-preceding doc text comes first; free-floating comments found
                    // inside the block (block tail etc.) are appended after it.
                    block.LeadingComment = MergeDocComments(doc, block.LeadingComment);
                    constBlock = block;
                    continue;
                }
                if (MatchKeyword("var"))
                {
                    var doc = commentAcc.Detach();
                    if (varBlock is not null)
                        Error(KsErrors.DuplicateDeclBlock, "Duplicate var block");
                    var block = ParseVarBlock();
                    block.LeadingComment = MergeDocComments(doc, block.LeadingComment);
                    varBlock = block;
                    continue;
                }
                var stmt = ParseStatement();
                stmt.LeadingComment = commentAcc.Detach();
                body.Add(stmt);
            }
        }
        catch (NestingLimitExceededException)
        {
            // B5c: expression/statement nesting exceeded MaxNestingDepth. Record the
            // error and return the partial program — never crash the process with a
            // StackOverflowException.
            Error(KsErrors.NestingTooDeep,
                $"Nesting depth exceeds the limit of {MaxNestingDepth}; expression/statement nesting is too deep");
        }

        return new KsProgram
        {
            ConstBlock = constBlock,
            VarBlock = varBlock,
            Body = body.ToImmutable(),
            SourceLine = 1,
            // Whatever remains in the accumulator at end of input has no following
            // statement or decl block — a file-end free-floating comment run.
            TrailingDocComment = commentAcc.Detach(),
        };
    }

    /// <summary>Joins a block-preceding doc text with an inside-block free-floating comment
    /// run, preserving source order (block doc first, inside-block appends after).</summary>
    private static string? MergeDocComments(string? before, string? after)
    {
        if (before is null) return after;
        if (after is null) return before;
        return before + "\n" + after;
    }

    // ── Decl blocks ──

    private TBlock ParseDeclBlock<TBlock, TRow>(
        Func<string?, TRow> rowParser,
        Func<ImmutableArray<TRow>, string?, TBlock> blockFactory)
        where TRow : KsNode
    {
        var decls = ImmutableArray.CreateBuilder<TRow>();
        var commentAcc = new CommentAccumulator();
        ExpectLBrace();
        while (!AtEnd && Current.Kind != KsTokenKind.RBrace)
        {
            if (Current.Kind == KsTokenKind.Indent) Advance();
            if (Current.Kind == KsTokenKind.RBrace) break;
            // A full-line comment accumulates as the leading comment of the NEXT decl
            // row (same "immediately preceding" semantics as statement comments — blank
            // lines emit no tokens, so they never break the run).
            if (Current.Kind == KsTokenKind.Comment)
            {
                commentAcc.Add(Current.Text);
                Advance();
                continue;
            }
            decls.Add(rowParser(commentAcc.Detach()));
            while (!AtEnd && Current.Kind != KsTokenKind.Indent
                          && Current.Kind != KsTokenKind.RBrace) Advance();
        }
        Match(KsTokenKind.RBrace);
        // A comment run that never led a decl row (block tail / free-floating) becomes
        // part of the block's doc comment.
        return blockFactory(decls.ToImmutable(), commentAcc.Detach());
    }

    private KsConstBlock ParseConstBlock() =>
        ParseDeclBlock<KsConstBlock, KsConstDecl>(
            ParseConstRow, (decls, freeDoc) => new KsConstBlock { Declarations = decls, LeadingComment = freeDoc });

    private KsVarBlock ParseVarBlock() =>
        ParseDeclBlock<KsVarBlock, KsVarDecl>(
            ParseVarRow, (decls, freeDoc) => new KsVarBlock { Declarations = decls, LeadingComment = freeDoc });

    private T ParseDeclRow<T>(string? leading, Func<string, string, string?, KsDictLiteral?, string, int, T> factory)
        where T : KsNode
    {
        var (typeTok, nameTok, initExpr, dictInit, src, trailing) = ParseDeclRowCore();
        var row = factory(typeTok.Text, nameTok.Text, initExpr, dictInit, src, typeTok.Line);
        if (row is KsConstDecl cd) { cd.LeadingComment = leading; cd.TrailingComment = trailing; }
        else if (row is KsVarDecl vd) { vd.LeadingComment = leading; vd.TrailingComment = trailing; }
        return row;
    }

    private KsConstDecl ParseConstRow(string? leading) =>
        ParseDeclRow<KsConstDecl>(leading, (type, name, init, dictInit, src, line) => new KsConstDecl
        {
            Name = name,
            Type = type,
            InitialValueExpression = init,
            DictInitializer = dictInit,
            SourceText = src,
            SourceLine = line,
        });

    private KsVarDecl ParseVarRow(string? leading) =>
        ParseDeclRow<KsVarDecl>(leading, (type, name, init, dictInit, src, line) => new KsVarDecl
        {
            Name = name,
            Type = type,
            InitialValueExpression = init,
            DictInitializer = dictInit,
            SourceText = src,
            SourceLine = line,
        });

    private (KsToken typeTok, KsToken nameTok, string? initExpr, KsDictLiteral? dictInit, string src, string? trailing) ParseDeclRowCore()
    {
        // Form: <type> <name> ['=' <initialiser>]
        var typeTok = Current.Kind == KsTokenKind.Identifier ? Advance() : Current;
        var nameTok = Current.Kind == KsTokenKind.Identifier ? Advance() : Current;
        if (typeTok.Kind != KsTokenKind.Identifier)
            Error(KsErrors.InvalidDeclaration, "Declaration must start with a type name", typeTok);
        if (nameTok.Kind != KsTokenKind.Identifier)
            Error(KsErrors.InvalidDeclaration, "Declaration must have a name after the type", nameTok);

        string? initExpr = null;
        KsDictLiteral? dictInit = null;
        string? trailing = null;
        if (Match(KsTokenKind.Assign))
        {
            if (typeTok.Text == "dict" && Current.Kind == KsTokenKind.LBrace)
            {
                // dict literal initialiser: {k: v, ...} (Dict-Type design §2.1)
                dictInit = ParseDictLiteral();
                initExpr = dictInit.SourceText;
            }
            else
            {
                // Scalar initialiser: LITERAL ONLY. No expressions/references — a decl-block
                // initialiser must be expressible as a BP definition-node payload (ConstValue /
                // VarInitialValue / DictNew pin DefaultValues), which precludes data edges to
                // other nodes. (Package/Dict-Type-Design.md §2.1 / §4.5.)
                if (IsScalarLiteralToken(Current.Kind))
                {
                    initExpr = ReconstructText(_tokens, _pos, _pos + 1).Trim();
                    Advance();
                    // Consume the row's inline comment (`int x = 5 // note`) BEFORE the
                    // "anything beyond a single literal" check — a comment must not be
                    // misread as a trailing expression (KS076 false positive).
                    trailing = TryConsumeComment();
                    // Reject anything beyond a single literal on the line (e.g. `42 + 1`, `x`).
                    if (trailing is null && !AtEnd && Current.Kind != KsTokenKind.Indent && Current.Kind != KsTokenKind.RBrace)
                        Error(KsErrors.DeclInitMustBeSingleLiteral, "Scalar var/const initialiser must be a single literal — expressions/references are not allowed in decl blocks");
                }
                else
                {
                    Error(KsErrors.DeclInitMustBeSingleLiteral, "Scalar var/const initialiser must be a literal — references/expressions are not allowed in decl blocks");
                    // Skip to end of line for error recovery.
                    while (!AtEnd && Current.Kind != KsTokenKind.Indent
                                  && Current.Kind != KsTokenKind.RBrace) Advance();
                }
            }
        }

        // Rows without an initialiser (or with a dict initialiser) still carry their
        // inline comment (`int counter // note`) — capture it here.
        trailing ??= TryConsumeComment();
        var src = $"{typeTok.Text} {nameTok.Text}{(initExpr is null ? "" : " = " + initExpr)}";
        return (typeTok, nameTok, initExpr, dictInit, src, trailing);
    }

    /// <summary>True for the six scalar-literal token kinds usable as a decl-block initialiser.</summary>
    private static bool IsScalarLiteralToken(KsTokenKind kind) =>
        kind is KsTokenKind.StringLiteral or KsTokenKind.IntegerLiteral or KsTokenKind.DoubleLiteral
            or KsTokenKind.CharLiteral or KsTokenKind.BooleanLiteral or KsTokenKind.NullLiteral;

    // ── Dict literal parsing (only valid as a const/var declaration initialiser) ──

    /// <summary>
    /// Parses a <c>{k: v, ...}</c> dict literal. Caller has consumed the leading <c>=</c> and
    /// verified <c>Current</c> is <c>LBrace</c>. Rejects nested dict/array values
    /// (Dict-Type design §2.4: flat scalars only).
    /// </summary>
    private KsDictLiteral ParseDictLiteral()
    {
        var startTok = Current;
        ExpectLBrace();  // consume '{'
        var entries = ImmutableArray.CreateBuilder<KsDictEntry>();

        if (Current.Kind == KsTokenKind.RBrace)
        {
            Advance();
            return new KsDictLiteral { Entries = [], SourceText = "{}", SourceLine = startTok.Line };
        }

        while (true)
        {
            var key = ParseDictKey(startTok.Line);
            if (!Match(KsTokenKind.Colon))
                Error(KsErrors.ExpectedColonAfterDictKey, "Expected ':' after dict key");
            var value = ParseDictValue(startTok.Line);
            entries.Add(new KsDictEntry { Key = key, Value = value });

            if (Match(KsTokenKind.Comma)) continue;
            if (Match(KsTokenKind.RBrace)) break;
            Error(KsErrors.ExpectedCommaOrRBraceInDict, "Expected ',' or '}' in dict literal");
            break;
        }

        var src = "{" + string.Join(", ", entries.Select(e =>
            $"{e.Key.SourceText}: {e.Value.SourceText}")) + "}";
        return new KsDictLiteral { Entries = entries.ToImmutable(), SourceText = src, SourceLine = startTok.Line };
    }

    private KsNode ParseDictKey(int line)
    {
        if (Current.Kind == KsTokenKind.StringLiteral)
        {
            var t = Advance();
            return new KsLiteral { Kind = KsLiteralKind.String, Value = t.Text, SourceText = KsScalarLiteralCodec.EncodeStringLiteral(t.Text), SourceLine = t.Line };
        }
        if (Current.Kind == KsTokenKind.Identifier)
        {
            var t = Advance();
            // Identifier key → string literal (syntactic sugar, equivalent to "key")
            return new KsLiteral { Kind = KsLiteralKind.String, Value = t.Text, SourceText = t.Text, SourceLine = t.Line };
        }
        Error(KsErrors.DictKeyMustBeStringOrIdentifier, "Dict key must be a string literal or identifier");
        Advance();
        return new KsLiteral { Kind = KsLiteralKind.String, Value = "?", SourceText = "\"?\"", SourceLine = line };
    }

    private KsNode ParseDictValue(int line)
    {
        switch (Current.Kind)
        {
            case KsTokenKind.StringLiteral:
            case KsTokenKind.IntegerLiteral:
            case KsTokenKind.DoubleLiteral:
            case KsTokenKind.CharLiteral:
            case KsTokenKind.BooleanLiteral:
            case KsTokenKind.NullLiteral:
                return ParseLiteralOrPlaceholder();
            case KsTokenKind.Identifier:
                // References (incl. const) are NOT allowed as dict values — a dict literal lives
                // in a decl block, whose values must be literals so BP definition nodes can carry
                // them as payloads (no data edges). (Dict-Type design §2.1 / §2.4.)
                Error(KsErrors.DictValueNoReferences, "Dict value must be a literal — references/expressions are not allowed in dict literals");
                Advance();
                return new KsLiteral { Kind = KsLiteralKind.Null, SourceText = "null", SourceLine = line };
            case KsTokenKind.LBrace:
                Error(KsErrors.NestedDictLiteralNotAllowed, "Nested dict literal is not allowed — use JSON format for nested structures");
                Advance();
                return new KsLiteral { Kind = KsLiteralKind.Null, SourceText = "null", SourceLine = line };
            default:
                // KS077 forbids const references as dict values — the message must not
                // suggest them (previously: "or const reference", self-contradictory).
                Error(KsErrors.DictValueMustBeScalarLiteral, "Dict value must be a scalar literal");
                Advance();
                return new KsLiteral { Kind = KsLiteralKind.Null, SourceText = "null", SourceLine = line };
        }
    }

    private void ExpectLBrace()
    {
        if (!Match(KsTokenKind.LBrace))
            Error(KsErrors.ExpectedLBraceAfterConstVar, "Expected '{' after const/var");
    }

    private static string ReconstructText(List<KsToken> tokens, int from, int toExclusive)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = from; i < toExclusive && i < tokens.Count; i++)
        {
            if (sb.Length > 0) sb.Append(' ');
            var tok = tokens[i];
            // Re-wrap literal tokens so the reconstructed text is valid KS / C# source.
            // StringLiteral.Text holds the *decoded* content (without surrounding quotes);
            // re-wrap via the shared codec (escape-symmetric) so e.g. `string bfCode = "..."`
            // round-trips correctly through Codegen (which emits InitialValueExpression
            // verbatim into C#).
            sb.Append(tok.Kind switch
            {
                KsTokenKind.StringLiteral => KsScalarLiteralCodec.EncodeStringLiteral(tok.Text),
                KsTokenKind.CharLiteral => tok.Value is char c
                    ? KsScalarLiteralCodec.EncodeCharLiteral(c)
                    : $"'{tok.Text}'",
                _ => tok.Text,
            });
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
                    _ => ParsePipelineOrAssignment(),
                };
            default:
                return ParsePipelineOrAssignment();
        }
    }

    private KsIf ParseIf()
    {
        // The body indent is keyed to the IF KEYWORD's own line indent (the just-consumed
        // Indent token), NOT to the last consumed indent: a multi-line condition header's
        // continuation lines sit at keywordIndent+1 and MUST NOT push the body deeper —
        // per the grammar, "续行段与 body 首行同缩进（header+1），以 `>` 开头区分续行 vs body"
        // (the `>` / comma continuations are consumed below, so capturing here is the
        // only correct anchor). W-9's strict Parse surfaced this: the old capture point
        // made every multi-line-header body KS062-empty.
        int keywordIndent = LastConsumedIndentLevel();
        var ifTok = Advance();  // 'if'
        var (cond, lastSegComment) = ParseHeaderPipelineExpression();
        if (!Match(KsTokenKind.Colon))
            Error(KsErrors.ExpectedColonAfterHeader, "Expected ':' after if-header expression");
        var trailing = TryConsumeComment();  // post-colon inline comment
        string? stmtTrailing = null;
        var lastComment = lastSegComment ?? trailing;
        if (lastComment is not null && cond is KsPipeline pipe)
            pipe.Segments[^1].Comment = lastComment;
        else
            stmtTrailing = trailing;
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
                    // `else if` is deliberately unsupported: it would break the KS↔IR
                    // bijection — both `else if c:` and `else:\n    if c:` lower to the
                    // same nested-If IR, so a round-trip would rewrite the text.
                    // Require the nested form instead.
                    Error(KsErrors.ElseIfUnsupported, "'else if' is not supported — write 'else:' followed by a nested 'if' block");
                }
                else
                {
                    if (!Match(KsTokenKind.Colon))
                        Error(KsErrors.ExpectedColonAfterHeader, "Expected ':' after 'else'");
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
        // Same anchor rule as ParseIf: keywordIndent = the switch keyword's own line
        // indent (captured before the header expression can consume continuation lines).
        int keywordIndent = LastConsumedIndentLevel();
        var swTok = Advance();  // 'switch'
        var selector = ParseExpression();
        if (!Match(KsTokenKind.Colon))
            Error(KsErrors.ExpectedColonAfterHeader, "Expected ':' after switch selector");
        TryConsumeComment();  // post-colon comment on the `switch` header line (not attached)
        int armIndent = keywordIndent + 1;
        var arms = ImmutableArray.CreateBuilder<ImmutableArray<KsStatement>>();
        var armLabels = ImmutableArray.CreateBuilder<int>();
        ImmutableArray<KsStatement> defaultBody = [];
        bool sawDefault = false;
        var commentAcc = new CommentAccumulator();

        while (Current.Kind == KsTokenKind.Indent && Current.IndentLevel == armIndent)
        {
            Advance();  // consume Indent(armIndent)
            // A full-line comment between arms accumulates as the leading comment of the
            // NEXT arm's first statement (same "immediately preceding" semantics as
            // ParseBody/ParseProgram — blank lines emit no tokens, so they never break
            // the run). Without this the comment would fall into the KS021 "expected
            // case label or 'default'" branch below (B5b).
            if (Current.Kind == KsTokenKind.Comment)
            {
                commentAcc.Add(Current.Text);
                Advance();
                continue;
            }
            if (MatchKeyword("default"))
            {
                if (sawDefault) Error(KsErrors.DuplicateDefaultArm, "Duplicate default arm");
                sawDefault = true;
                if (!Match(KsTokenKind.Colon))
                    Error(KsErrors.ExpectedColonAfterCaseLabel, "Expected ':' after 'default'");
                defaultBody = ParseArmBody(armIndent);
                if (defaultBody.Length > 0)
                    defaultBody = defaultBody.SetItem(0,
                        defaultBody[0] with { LeadingComment = MergeDocComments(commentAcc.Detach(), defaultBody[0].LeadingComment) });
            }
            else if (Current.Kind == KsTokenKind.IntegerLiteral)
            {
                // Capture the arm label value (value-match semantics). The tokenizer
                // guarantees IntegerLiteral tokens carry an int Value.
                int label = (int)Current.Value!;
                Advance();
                if (!Match(KsTokenKind.Colon))
                    Error(KsErrors.ExpectedColonAfterCaseLabel, "Expected ':' after case label");
                armLabels.Add(label);
                var arm = ParseArmBody(armIndent);
                if (arm.Length > 0)
                    arm = arm.SetItem(0,
                        arm[0] with { LeadingComment = MergeDocComments(commentAcc.Detach(), arm[0].LeadingComment) });
                arms.Add(arm);
            }
            else
            {
                Error(KsErrors.ExpectedCaseLabelOrDefault, "Expected case label or 'default' in switch arm");
                while (!AtEnd && Current.Kind != KsTokenKind.Indent) Advance();
            }
        }

        return new KsSwitch
        {
            Selector = selector,
            Arms = arms.ToImmutable(),
            ArmLabels = armLabels.ToImmutable(),
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
        // Anchor rule as ParseIf: keywordIndent = the forEach keyword's own line indent.
        int keywordIndent = LastConsumedIndentLevel();
        var feTok = Advance();  // 'forEach'
        // Source accepts pipeline expressions (like if/while conditions), so
        // `forEach loopMax > Range(0, _, 1) as i` is valid — the entire pipeline
        // between `forEach` and `as` is the source expression.
        var (source, lastSegComment) = ParseHeaderPipelineExpression();
        if (!MatchKeyword("as"))
            Error(KsErrors.ExpectedAsAfterForEach, "Expected 'as' after forEach source");
        if (Current.Kind != KsTokenKind.Identifier)
            Error(KsErrors.ExpectedItemNameAfterAs, "Expected item name after 'as'");
        var itemName = Advance().Text;
        if (!Match(KsTokenKind.Colon))
            Error(KsErrors.ExpectedColonAfterHeader, "Expected ':' after forEach header");
        var trailing = TryConsumeComment();
        string? stmtTrailing = null;
        var lastComment = lastSegComment ?? trailing;
        if (lastComment is not null && source is KsPipeline pipe)
            pipe.Segments[^1].Comment = lastComment;
        else
            stmtTrailing = trailing;
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
        // Anchor rule as ParseIf: keywordIndent = the while keyword's own line indent.
        int keywordIndent = LastConsumedIndentLevel();
        var whTok = Advance();  // 'while'
        var (cond, lastSegComment) = ParseHeaderPipelineExpression();
        if (!Match(KsTokenKind.Colon))
            Error(KsErrors.ExpectedColonAfterHeader, "Expected ':' after while-header expression");
        var trailing = TryConsumeComment();
        string? stmtTrailing = null;
        var lastComment = lastSegComment ?? trailing;
        if (lastComment is not null && cond is KsPipeline pipe)
            pipe.Segments[^1].Comment = lastComment;
        else
            stmtTrailing = trailing;
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

    // ── Pipelines and expressions ──

    private KsStatement ParsePipelineOrAssignment()
    {
        // A pipeline line: <src> (',' <src>)* ('>' <segment>)* ('=' <name>)? ';'?
        // A bare call:    <call>   (lowered to a one-source pipeline with one call segment)
        //
        // forEach is NOT a valid pipeline segment target — it is a prefix keyword
        // statement: `forEach <source> as <item>`. Use that form instead.
        var (sources, segments, lastSegComment) = ParsePipelineCore(ParseExpression(), "as a pipeline segment");

        // Capture point A — a trailing comment after the inline sources/segments. In a
        // multi-line pipeline this sits on the source line (e.g. `a, b // src cmt`); in
        // a single-line pipeline with no continuation it is the end-of-statement comment.
        // It is resolved against capture point C below (they are mutually exclusive).
        // Must run BEFORE the continuation loop — the comment precedes the continuation
        // lines, so the loop would otherwise never see its Indent + Pipe.
        string? sourceTrailing = TryConsumeComment();
        ParsePipelineContinuations(segments, "as a pipeline segment", ref lastSegComment);

        // Terminal assignment `= name` becomes a variable-tap segment.
        if (Match(KsTokenKind.Assign))
        {
            if (Current.Kind != KsTokenKind.Identifier)
                Error(KsErrors.ExpectedVariableNameAfterAssign, "Expected variable name after '='");
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

        // KS053: reject bare statements that are neither a bare call (`Print("hello")`),
        // a single identifier/literal read (a no-op exec anchor — the BP-side counterpart
        // of a usage node on the exec chain without data edges), nor a pipeline with
        // segments. Multi-source bare lists (`a, b`) and placeholder-only lines (`_`)
        // stay invalid. Note: Error() is non-fatal — diagnostics are collected, parsing
        // continues (a bare multi-source list still yields a partial KsPipeline).
        if (segments.Count == 0)
        {
            bool isBareCall = sources.Count == 1 && sources[0] is KsCall;
            bool isNoOpRead = sources.Count == 1 && sources[0] is KsIdentifier or KsLiteral;
            if (!isBareCall && !isNoOpRead)
            {
                Error(KsErrors.BareStatementInvalid,
                    "Bare statement is not valid; a pipeline must contain at least one '>' " +
                    "segment or '= name' assignment, be a single function call, or a single " +
                    "identifier/literal read (no-op exec anchor)");
            }
        }

        // Capture point C — end-of-statement trailing comment for the single-line form
        // (after the last token on the one physical line). Mutually exclusive with A:
        // when a multi-line continuation ran, the last segment's comment was captured at
        // point B and the current token is a new-line Indent (not a Comment).
        string? endTrailing = TryConsumeComment();

        return new KsPipeline
        {
            Sources = sources.ToImmutable(),
            Segments = segments.ToImmutable(),
            SourceLine = sources[0].SourceLine,
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
            Error(KsErrors.ExpectedSegmentNameAfterPipe, "Expected segment name after '>'");
            return new KsPipelineSegment { Target = "?", SourceLine = Current.Line };
        }
        var nameTok = Advance();
        var args = ImmutableArray.CreateBuilder<KsNode>();
        bool isCall = false;

        if (Match(KsTokenKind.LParen))
        {
            isCall = true;
            if (Current.Kind != KsTokenKind.RParen)
            {
                args.Add(ParseLiteralOrPlaceholder());
                while (Match(KsTokenKind.Comma))
                    args.Add(ParseLiteralOrPlaceholder());
            }
            if (!Match(KsTokenKind.RParen))
                Error(KsErrors.ExpectedRParenToCloseArgs, "Expected ')' to close call arguments");
        }

        return new KsPipelineSegment
        {
            Target = nameTok.Text,
            Args = args.ToImmutable(),
            // Never mark a `> name` as a tap here — only `= name` becomes a tap.
            // A bare `> name` is a call with no args (the pipeline value is the
            // implicit single arg via `_`). Consumers (codegen/renderer/type-inferer)
            // classify var taps structurally via KsSegmentClassifier, so this flag is
            // intentionally left false for the `> name` form.
            IsVariableTap = false,
            SourceLine = nameTok.Line,
            SourceText = isCall
                ? $"{nameTok.Text}({string.Join(", ", args.Select(a => a.SourceText))})"
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
                { var t = Advance(); return new KsLiteral { Kind = KsLiteralKind.String, Value = t.Value, SourceText = KsScalarLiteralCodec.EncodeStringLiteral(t.Value as string), SourceLine = t.Line }; }
            case KsTokenKind.IntegerLiteral:
                { var t = Advance(); return new KsLiteral { Kind = KsLiteralKind.Integer, Value = t.Value, SourceText = t.Text, SourceLine = t.Line }; }
            case KsTokenKind.DoubleLiteral:
                { var t = Advance(); return new KsLiteral { Kind = KsLiteralKind.Double, Value = t.Value, SourceText = t.Text, SourceLine = t.Line }; }
            case KsTokenKind.CharLiteral:
                { var t = Advance(); return new KsLiteral { Kind = KsLiteralKind.Char, Value = t.Value, SourceText = t.Value is char c ? KsScalarLiteralCodec.EncodeCharLiteral(c) : $"'{t.Value}'", SourceLine = t.Line }; }
            case KsTokenKind.BooleanLiteral:
                { var t = Advance(); return new KsLiteral { Kind = KsLiteralKind.Boolean, Value = t.Value, SourceText = t.Text, SourceLine = t.Line }; }
            case KsTokenKind.NullLiteral:
                { var t = Advance(); return new KsLiteral { Kind = KsLiteralKind.Null, Value = null, SourceText = "null", SourceLine = t.Line }; }
            case KsTokenKind.Placeholder:
                { var t = Advance(); return new KsPlaceholder { SourceText = "_", SourceLine = t.Line }; }
            default:
                Error(KsErrors.FunctionArgsOnlyLiteralsOrPlaceholders, $"Function arguments may only be literals or '_' placeholders (v6.0 rule); got: {Current.Kind} '{Current.Text}'. Use pipeline form: 'value > Func(...)'");
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
        // B5c: depth guard — parenthesised pipelines recurse via
        // ParseHeaderPipelineExpression; deep paren nesting must report KS078
        // instead of overflowing the stack.
        EnterNesting();
        try
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
                            if (Current.Kind != KsTokenKind.RParen)
                            {
                                args.Add(ParseLiteralOrPlaceholder());
                                while (Match(KsTokenKind.Comma))
                                    args.Add(ParseLiteralOrPlaceholder());
                            }
                            if (!Match(KsTokenKind.RParen))
                                Error(KsErrors.ExpectedRParenToCloseCallArgs, "Expected ')' to close call arguments");
                            return new KsCall
                            {
                                MethodName = t.Text,
                                FullMethodName = t.Text,
                                Args = args.ToImmutable(),
                                SourceText = $"{t.Text}({string.Join(", ", args.Select(a => a.SourceText))})",
                                SourceLine = t.Line,
                            };
                        }
                        return new KsIdentifier { Name = t.Text, SourceText = t.Text, SourceLine = t.Line };
                    }
                case KsTokenKind.LParen:
                    {
                        // Parenthesised pipeline source: (a > Func) — Dict-Type design §8.3.
                        // Recursively parse the inner pipeline expression, then expect ')'.
                        var t = Advance();  // consume '('
                        var (inner, _) = ParseHeaderPipelineExpression();
                        if (!Match(KsTokenKind.RParen))
                            Error(KsErrors.ExpectedRParenToCloseParenPipeline, "Expected ')' to close parenthesised pipeline source");
                        // Wrap a pipeline's source text in parens for lossless round-trip.
                        if (inner is KsPipeline)
                            inner.SourceText = $"({inner.SourceText})";
                        else
                            inner.SourceLine = t.Line;
                        return inner;
                    }
                default:
                    Error(KsErrors.UnexpectedTokenInExpression, $"Unexpected token in expression: {Current.Kind} '{Current.Text}'");
                    Advance();
                    return new KsIdentifier { Name = "?", SourceText = "?", SourceLine = Current.Line };
            }
        }
        finally
        {
            _nestingDepth--;
        }
    }

    /// <summary>
    /// Shared core of the two pipeline paths — statement pipelines
    /// (<see cref="ParsePipelineOrAssignment"/>) and control-flow header pipelines.
    /// Parses the comma-continued source list (KS066 continuation guard + KS065
    /// comment rejection) and the inline <c>&gt; segment</c> list. The statement path
    /// calls <see cref="ParsePipelineContinuations"/> AFTER its capture-point-A
    /// comment check, which must run between the segment list and the multi-line
    /// continuation loop (a source-line trailing comment precedes the continuation
    /// lines); the header path calls it immediately. Returns the source/segment
    /// builders (the statement path mutates <c>segments</c> with the terminal
    /// <c>= name</c> tap) plus the last segment's inline comment (the header path
    /// surfaces it; the statement path ignores it).
    /// </summary>
    private (ImmutableArray<KsNode>.Builder Sources, ImmutableArray<KsPipelineSegment>.Builder Segments, string? LastSegComment)
        ParsePipelineCore(KsNode firstSource, string forEachContext)
    {
        var sources = ImmutableArray.CreateBuilder<KsNode>();
        sources.Add(firstSource);
        int stmtIndent = LastConsumedIndentLevel();
        while (true)
        {
            if (!Match(KsTokenKind.Comma)) break;

            // Inline comment right after the comma attaches to the preceding source
            // (`a, // cmt` → a.Comment), enabling multi-line source lists.
            var srcTrailing = TryConsumeComment();
            if (srcTrailing is not null && sources.Count > 0)
                sources[^1].Comment = srcTrailing;

            // Comma line-break: the next line continues the source list. Strict indent
            // rule: the continuation line must be indented strictly deeper than the
            // statement (statement indent + 1), mirroring the segment-continuation rule.
            bool invalidContinuation = false;
            while (Current.Kind == KsTokenKind.Indent && Peek(1).Kind != KsTokenKind.Pipe)
            {
                if (Current.IndentLevel < stmtIndent + 1)
                {
                    Error(KsErrors.ContinuationIndentTooShallow,
                        $"多行管道续源行缩进必须大于语句缩进（语句缩进 {stmtIndent}，实际 {Current.IndentLevel}）");
                    // Recover: skip to the next line so it re-parses as its own statement.
                    while (!AtEnd && Current.Kind != KsTokenKind.Indent) Advance();
                    invalidContinuation = true;
                    break;
                }
                // KS065: a full-line comment between source continuations is rejected —
                // the trailing comma means the pipeline is unfinished, so the comment
                // line is necessarily inside the pipeline.
                if (Peek(1).Kind == KsTokenKind.Comment)
                {
                    Error(KsErrors.CommentBetweenContinuations, "多行管道续行之间不允许整行注释；注释请放在语句前或续行段后（行内注释）。");
                    Advance();  // consume Indent
                    Advance();  // consume Comment
                    continue;
                }
                Advance();  // consume Indent
                break;
            }
            if (invalidContinuation) break;
            sources.Add(ParseExpression());
        }

        var segments = ImmutableArray.CreateBuilder<KsPipelineSegment>();
        string? lastSegComment = null;
        while (Match(KsTokenKind.Pipe))
        {
            if (RejectForEachInPipeline(forEachContext))
                break;
            // An inline comment right after a same-line segment attaches to THAT
            // segment (the nearest node), never to the statement — `a > FB // cmt`
            // → FB.Comment. Capture point A in the statement path only sees comments
            // that no segment could own (bare calls / source-list tails).
            var seg = ParseSegment();
            var cmt = TryConsumeComment();
            if (cmt is not null)
                seg.Comment = cmt;
            lastSegComment = cmt;
            segments.Add(seg);
        }

        return (sources, segments, lastSegComment);
    }

    /// <summary>
    /// Multi-line pipeline continuation loop, shared by the two pipeline paths: lines
    /// starting with Indent + Pipe continue the current pipeline (each segment on its
    /// own line — a prerequisite for per-segment comment preservation, Phase B).
    /// Continuation lines may carry several segments; the inline comment lands on the
    /// LAST segment of the line (capture B). A full-line comment BETWEEN continuation
    /// lines is rejected (KS065). The caller invokes this AFTER capture point A on the
    /// statement path (see <see cref="ParsePipelineCore"/>).
    /// </summary>
    private void ParsePipelineContinuations(
        ImmutableArray<KsPipelineSegment>.Builder segments, string forEachContext, ref string? lastSegComment)
    {
        while (true)
        {
            if (Current.Kind == KsTokenKind.Indent && Peek(1).Kind == KsTokenKind.Pipe)
            {
                Advance(); // consume Indent
                Advance(); // consume Pipe
                while (true)
                {
                    if (RejectForEachInPipeline(forEachContext))
                        break;
                    var seg = ParseSegment();
                    var cmt = TryConsumeComment();
                    if (cmt is not null)
                        seg.Comment = cmt;
                    lastSegComment = cmt;
                    segments.Add(seg);
                    if (!Match(KsTokenKind.Pipe))
                        break;
                }
                continue;
            }

            // KS065: a full-line comment BETWEEN continuation lines is rejected — a leading
            // comment belongs to the whole statement (one line ⇔ one data subgraph); only
            // per-segment inline comments are allowed inside a multi-line pipeline.
            if (Current.Kind == KsTokenKind.Indent && Peek(1).Kind == KsTokenKind.Comment
                && HasContinuationAfterCommentLine())
            {
                Error(KsErrors.CommentBetweenContinuations, "多行管道续行之间不允许整行注释；注释请放在语句前或续行段后（行内注释）。");
                Advance(); // consume Indent
                Advance(); // consume Comment
                continue;
            }
            break;
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
        var (sources, segments, lastSegComment) = ParsePipelineCore(firstSource, "in a pipeline expression");
        ParsePipelineContinuations(segments, "in a pipeline expression", ref lastSegComment);

        if (segments.Count == 0)
            Error(KsErrors.MultipleSourcesNeedSegment, "Multiple sources in condition require a '>' pipeline segment");

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
        // B5c: depth guard — nested control-flow statements recurse through here;
        // pathological nesting must report KS078 instead of overflowing the stack.
        EnterNesting();
        try
        {
            var body = ImmutableArray.CreateBuilder<KsStatement>();
            var commentAcc = new CommentAccumulator();
            while (Current.Kind == KsTokenKind.Indent && Current.IndentLevel == bodyIndent)
            {
                Advance();  // consume Indent(bodyIndent)
                // A full-line comment emits Indent(bodyIndent) + Comment. Accumulate it as a
                // leading comment for the next statement in this body.
                if (Current.Kind == KsTokenKind.Comment)
                {
                    commentAcc.Add(Current.Text);
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
                stmt.LeadingComment = commentAcc.Detach();
                body.Add(stmt);
            }
            if (body.Count == 0)
                Error(KsErrors.EmptyBody, $"{context} body is empty");
            return body.ToImmutable();
        }
        finally
        {
            _nestingDepth--;
        }
    }
}