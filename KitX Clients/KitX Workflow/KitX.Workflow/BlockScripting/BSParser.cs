using System.Globalization;
using Superpower;
using Superpower.Display;
using Superpower.Model;
using Superpower.Parsers;
using Superpower.Tokenizers;
using KitX.Core.Contract.Workflow;
using KitX.Workflow.Models;
using KitX.Workflow.Models.Statements;

namespace KitX.Workflow.BlockScripting;

// ─────────────────────────────────────────────────────────────────────────────
// BSParser — Superpower-based parser for BlockScript v5.0.
//
// Replaces the Roslyn C# parser + PipelinePreScanner (__pipe/__seg hack) +
// BSExpressionAdapter.FromRoslyn bridge. Produces the same BSExpression AST and
// BlockDefinition models the converters consume, with token-level error messages
// ("unexpected token X, expected Y") instead of Roslyn's C#-style diagnostics.
//
// Design (see Package/BlockScriptGrammarRule.md §十 EBNF, 28 productions):
//   1. TokenizerBuilder produces a flat token stream (BSToken), skipping whitespace
//      and line comments. Keywords (int/true/null/...) are NOT special-cased by the
//      tokenizer — they come through as Identifier tokens and are disambiguated in
//      the parser layer via Token.EqualToValue.
//   2. TokenListParser combinators build the BSExpression tree (Literal/Placeholder/
//      Identifier/FunctionCall/Binary) and the statement layer (PipelineStatement,
//      FlowControlStatement via builtin registry ExtractStatement).
//   3. Branch/ForLoop/Switch/Goto/Break are ordinary Identifiers → BSCall; the builtin
//      registry decides whether to produce a FlowControlStatement. This keeps the
//      parser extension-neutral: adding a control-flow builtin needs no parser change.
//
// Roslyn is NOT used here. It retains its C# emission + compilation duties only.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Token kinds for the BlockScript tokenizer.</summary>
/// <remarks>
/// Keyword kinds are NOT included for Branch/ForLoop/Switch/Goto/Break — those are
/// ordinary builtins parsed as Identifier → BSCall and dispatched via the registry.
/// Only lexical keywords (type names + literal keywords) get their own kinds so the
/// parser can distinguish them from user identifiers.
/// </remarks>
public enum BSToken
{
    [Token(Description = "identifier")] Identifier,
    [Token(Description = "integer literal")] IntegerLiteral,
    [Token(Description = "number")] DoubleLiteral,
    [Token(Description = "string")] StringLiteral,
    [Token(Description = "character literal")] CharLiteral,

    [Token(Category = "keyword", Example = "int")] Int,
    [Token(Category = "keyword", Example = "float")] Float,
    [Token(Category = "keyword", Example = "double")] Double,
    [Token(Category = "keyword", Example = "bool")] Bool,
    [Token(Category = "keyword", Example = "string")] StringKW,
    [Token(Category = "keyword", Example = "char")] CharKW,
    [Token(Category = "keyword", Example = "dynamic")] DynamicKW,
    [Token(Category = "keyword", Example = "true")] True,
    [Token(Category = "keyword", Example = "false")] False,
    [Token(Category = "keyword", Example = "null")] Null,

    [Token(Category = "operator", Example = ">")] Pipe,
    [Token(Category = "operator", Example = "+")] Plus,
    [Token(Category = "punctuation", Example = ",")] Comma,
    [Token(Category = "punctuation", Example = ";")] Semicolon,
    [Token(Category = "punctuation", Example = "(")] LParen,
    [Token(Category = "punctuation", Example = ")")] RParen,
    [Token(Category = "operator", Example = "=")] Assign,
    [Token(Category = "punctuation", Example = ".")] Dot,
    [Token(Category = "identifier", Example = "_")] Placeholder,
}

/// <summary>
/// Static BlockScript v5.0 parser. Two-phase (tokenizer + token-list parser) built on
/// Superpower. Public entry points: <see cref="Tokenize"/>, <see cref="ParseBlock"/>,
/// <see cref="ParseExpression"/>.
/// </summary>
public static class BSParser
{
    // ─── Tokenizer ──────────────────────────────────────────────────────────

    /// <summary>
    /// A line comment: <c>//</c> through end of line. Tokenized away as ignored trivia.
    /// v5.0 §9: comments are retained via leading-comment anchoring handled by the
    /// extractor (this parser only needs to skip them so they don't pollute tokens).
    /// </summary>
    static readonly TextParser<TextSpan> LineComment =
        Span.EqualTo("//")
            .IgnoreThen(Span.WithoutAny(ch => ch == '\n' || ch == '\r'))
            .Or(Span.EqualTo("//").Value(TextSpan.None));

    /// <summary>
    /// A C-style char literal: <c>'a'</c>, <c>'\n'</c>, <c>'\\'</c>, <c>'\''</c>.
    /// Mirrors the PipelinePreScanner.SkipCharLiteral rules the old prescanner handled.
    /// Returns the full literal text including quotes; the value is decoded in ParseCharValue.
    /// </summary>
    static readonly TextParser<TextSpan> CharLiteralText =
        Span.MatchedBy(
            Character.EqualTo('\'')
                .IgnoreThen(
                    (Span.EqualTo("\\'").Try()
                        .Or(Span.EqualTo("\\\\").Try())
                        .Or(Span.MatchedBy(Character.ExceptIn('\'', '\\', '\r', '\n'))))
                    .AtLeastOnce())
                .IgnoreThen(Character.EqualTo('\'')));

    /// <summary>
    /// A double literal: digits, a dot, digits (e.g. <c>3.14</c>). Requires the dot so plain
    /// integers (<c>42</c>) don't get mis-tokenized as doubles — <see cref="Numerics.DecimalDouble"/>
    /// alone accepts integer-only input, so we anchor on the dot explicitly.
    /// </summary>
    static readonly TextParser<double> DoubleWithDot =
        Numerics.Natural
            .Then(whole => Character.EqualTo('.')
                .IgnoreThen(Numerics.Natural.OptionalOrDefault(TextSpan.None))
                .Select(frac => double.Parse($"{whole.ToStringValue()}.{frac.ToStringValue()}", CultureInfo.InvariantCulture)));

    /// <summary>The single shared tokenizer instance.</summary>
    public static Tokenizer<BSToken> Tokenizer { get; } =
        new TokenizerBuilder<BSToken>()
            .Ignore(Span.WhiteSpace)
            .Ignore(LineComment)
            // Order matters: keywords must match before the generic identifier rule.
            // IntegerLiteral must match before DoubleLiteral-with-dot would otherwise be tried
            // (not strictly necessary now that DoubleWithDot requires a dot, but kept for clarity).
            .Match(Span.EqualTo("int"), BSToken.Int, requireDelimiters: true)
            .Match(Span.EqualTo("float"), BSToken.Float, requireDelimiters: true)
            .Match(Span.EqualTo("double"), BSToken.Double, requireDelimiters: true)
            .Match(Span.EqualTo("bool"), BSToken.Bool, requireDelimiters: true)
            .Match(Span.EqualTo("string"), BSToken.StringKW, requireDelimiters: true)
            .Match(Span.EqualTo("char"), BSToken.CharKW, requireDelimiters: true)
            .Match(Span.EqualTo("dynamic"), BSToken.DynamicKW, requireDelimiters: true)
            .Match(Span.EqualTo("true"), BSToken.True, requireDelimiters: true)
            .Match(Span.EqualTo("false"), BSToken.False, requireDelimiters: true)
            .Match(Span.EqualTo("null"), BSToken.Null, requireDelimiters: true)
            // Placeholder must match before Identifier (which excludes "_" via Where, but
            // having an explicit Placeholder rule is clearer and avoids the Where fragility).
            .Match(Span.EqualTo("_"), BSToken.Placeholder, requireDelimiters: true)
            .Match(Identifier.CStyle, BSToken.Identifier, requireDelimiters: true)
            .Match(DoubleWithDot, BSToken.DoubleLiteral, requireDelimiters: true)
            .Match(Numerics.Natural, BSToken.IntegerLiteral, requireDelimiters: true)
            .Match(QuotedString.CStyle, BSToken.StringLiteral)
            .Match(Span.MatchedBy(CharLiteralText), BSToken.CharLiteral)
            .Match(Character.EqualTo('>'), BSToken.Pipe)
            .Match(Character.EqualTo('+'), BSToken.Plus)
            .Match(Character.EqualTo(','), BSToken.Comma)
            .Match(Character.EqualTo(';'), BSToken.Semicolon)
            .Match(Character.EqualTo('('), BSToken.LParen)
            .Match(Character.EqualTo(')'), BSToken.RParen)
            .Match(Character.EqualTo('='), BSToken.Assign)
            .Match(Character.EqualTo('.'), BSToken.Dot)
            .Build();

    /// <summary>Tokenize source text. Throws <see cref="ParseException"/> on bad input.</summary>
    public static TokenList<BSToken> Tokenize(string source) => Tokenizer.Tokenize(source);

    // ─── Expression layer ───────────────────────────────────────────────────
    // Grammar (EBNF §十):
    //   Expression   = Literal | Placeholder | Identifier | FunctionCall | Binary
    //   FunctionCall = (Identifier {"." Identifier}) "(" [ArgumentList] ")"
    //   Binary       = Expression ("+" Expression)+    -- only "+", left-assoc
    // A lone dotted receiver without "(" (rare) falls back to BSIdentifier carrying
    // the full dotted path, matching BSExpressionAdapter's MemberAccess handling.

    static readonly TokenListParser<BSToken, BSExpression> Literal =
        Token.EqualTo(BSToken.StringLiteral).Apply(QuotedString.CStyle).Select(s => (BSExpression)new BSLiteral
        {
            Kind = BSLiteralKind.String,
            Value = s,
            SourceText = $"\"{s}\"",
        })
        .Or(Token.EqualTo(BSToken.IntegerLiteral).Select(t => (BSExpression)new BSLiteral
        {
            Kind = BSLiteralKind.Integer,
            Value = int.Parse(t.ToStringValue(), CultureInfo.InvariantCulture),
            SourceText = t.Span.ToStringValue(),
        }))
        .Or(Token.EqualTo(BSToken.DoubleLiteral).Apply(DoubleWithDot).Select(d => (BSExpression)new BSLiteral
        {
            Kind = BSLiteralKind.Double,
            Value = d,
            SourceText = d.ToString(CultureInfo.InvariantCulture),
        }))
        .Or(Token.EqualTo(BSToken.CharLiteral).Select(t => (BSExpression)new BSLiteral
        {
            Kind = BSLiteralKind.Char,
            Value = ParseCharValue(t.ToStringValue()),
            SourceText = t.Span.ToStringValue(),
        }))
        .Or(Token.EqualTo(BSToken.True).Select(t => (BSExpression)new BSLiteral
        {
            Kind = BSLiteralKind.Boolean,
            Value = true,
            SourceText = "true",
        }))
        .Or(Token.EqualTo(BSToken.False).Select(t => (BSExpression)new BSLiteral
        {
            Kind = BSLiteralKind.Boolean,
            Value = false,
            SourceText = "false",
        }))
        .Or(Token.EqualTo(BSToken.Null).Select(t => (BSExpression)new BSLiteral
        {
            Kind = BSLiteralKind.Null,
            Value = null,
            SourceText = "null",
        }));

    static readonly TokenListParser<BSToken, BSExpression> Placeholder =
        Token.EqualTo(BSToken.Placeholder).Select(t => (BSExpression)new BSPlaceholder { Index = 0, SourceText = "_" });

    /// <summary>
    /// A dotted receiver path: <c>Foo</c>, <c>Plugin.Sub.Method</c>. Produces the textual
    /// short name (last segment) plus the full dotted path. Used as the call target before
    /// the optional <c>(args)</c> is matched.
    /// </summary>
    static readonly TokenListParser<BSToken, (string ShortName, string FullName)> DottedName =
        Token.EqualTo(BSToken.Identifier)
            .Then(first => Token.EqualTo(BSToken.Dot).IgnoreThen(Token.EqualTo(BSToken.Identifier)).Many()
                .Select(rest =>
                {
                    var firstText = first.ToStringValue();
                    return rest.Length == 0
                        ? (firstText, firstText)
                        : (rest[^1].ToStringValue(), firstText + "." + string.Join(".", rest.Select(r => r.ToStringValue())));
                }));

    static readonly TokenListParser<BSToken, BSExpression> FunctionCallOrIdentifier =
        DottedName.Then(name =>
            // Match "( [args] )" optionally. When parens absent, treat as bare identifier.
            Token.EqualTo(BSToken.LParen)
                .Then(_lp => ArgumentList.OptionalOrDefault(Array.Empty<BSExpression>())
                    .Then(args => Token.EqualTo(BSToken.RParen).Select(_rp => (BSExpression[]?)args)))
                .OptionalOrDefault(null)
                .Select(args => BuildCallOrIdentifier(name, args)));

    static readonly TokenListParser<BSToken, BSExpression[]> ArgumentList =
        Parse.Ref(() => AddSub!)
            .Then(first => Token.EqualTo(BSToken.Comma).IgnoreThen(Parse.Ref(() => AddSub!)).Many()
                .Select(rest => new[] { first }.Concat(rest).ToArray()));

    /// <summary>
    /// The "+" binary operator (only "+" is meaningful in BS — comparison operators are
    /// forbidden, so they never reach this layer). Left-associative; flattened into a
    /// single BSBinary node per the Roslyn adapter's left-leaning chain shape.
    /// </summary>
    static readonly TokenListParser<BSToken, string> PlusOp =
        Token.EqualTo(BSToken.Plus).Select(_ => "+");

    static readonly TokenListParser<BSToken, BSExpression> AddSub =
        Parse.Chain(
            PlusOp,
            Literal.Or(Placeholder).Or(FunctionCallOrIdentifier),
            (op, left, right) => new BSBinary { Left = left, Right = right, Operator = op, SourceText = $"{left.SourceText} {op} {right.SourceText}" });

    /// <summary>The top-level expression entry. Exposed for <see cref="ParseExpression"/>.</summary>
    public static readonly TokenListParser<BSToken, BSExpression> Expression = AddSub.AtEnd();

    // ─── Statement layer ────────────────────────────────────────────────────

    static readonly TokenListParser<BSToken, BSExpression[]> SourceList =
        Parse.Ref(() => AddSub!)
            .Then(first => Token.EqualTo(BSToken.Comma).IgnoreThen(Parse.Ref(() => AddSub!)).Many()
                .Select(rest => new[] { first }.Concat(rest).ToArray()));

    /// <summary>A pipeline target — either a function call or a bare identifier (variable tap).</summary>
    static readonly TokenListParser<BSToken, BSCall> Target =
        DottedName.Then(name =>
            Token.EqualTo(BSToken.LParen)
                .Then(_lp => ArgumentList.OptionalOrDefault(Array.Empty<BSExpression>())
                    .Then(args => Token.EqualTo(BSToken.RParen).Select(_rp => (BSExpression[]?)args)))
                .OptionalOrDefault(null)
                .Select(args => BuildTargetCall(name, args)));

    static readonly TokenListParser<BSToken, BSPipeline> PipelineStatement =
        from sources in SourceList
        from targets in (
            Token.EqualTo(BSToken.Pipe).IgnoreThen(Target).AtLeastOnce()
        ).OptionalOrDefault(Array.Empty<BSCall>())
        from _semi in Token.EqualTo(BSToken.Semicolon)
        select new BSPipeline
        {
            Sources = new List<BSExpression>(sources),
            Targets = new List<BSCall>(targets),
            SourceText = RenderPipelineText(new List<BSExpression>(sources), new List<BSCall>(targets)),
        };

    /// <summary>
    /// A single statement: pipeline (with or without targets) or bare function call
    /// followed by <c>;</c>. The builtin registry is consulted downstream in
    /// <see cref="ParseBlock"/> to decide whether a bare call is a control-flow function.
    /// </summary>
    public static readonly TokenListParser<BSToken, BSExpression> Statement =
        PipelineStatement.Try().Select(p => (BSExpression)p)
        .Or(SourceList.Then(sources => Token.EqualTo(BSToken.Semicolon)
            .Select(_ => (BSExpression)new BSPipeline
            {
                Sources = new List<BSExpression>(sources),
                Targets = new List<BSCall>(),
                SourceText = RenderPipelineText(new List<BSExpression>(sources), new List<BSCall>()),
            })));

    /// <summary>
    /// Zero or more statements ending at end-of-input. Each statement is a pipeline form
    /// (possibly with zero targets — a bare call like <c>Print(x);</c>). The builtin
    /// registry dispatch happens in <see cref="ParseBlock"/>, not here, so this parser is
    /// purely syntactic.
    /// </summary>
    public static readonly TokenListParser<BSToken, List<BSExpression>> StatementList =
        Statement.Many().Select(arr => new List<BSExpression>(arr)).AtEnd();

    // ─── Declaration layer (ConstBlock / PubVarBlock / ##BlockVars) ──────────

    static readonly TokenListParser<BSToken, string> TypeKeyword =
        Token.EqualTo(BSToken.Int).Or(Token.EqualTo(BSToken.Float))
            .Or(Token.EqualTo(BSToken.Double)).Or(Token.EqualTo(BSToken.Bool))
            .Or(Token.EqualTo(BSToken.StringKW)).Or(Token.EqualTo(BSToken.CharKW))
            .Or(Token.EqualTo(BSToken.DynamicKW))
            .Select(t => t.ToStringValue());

    static readonly TokenListParser<BSToken, VariableDeclaration> VariableDeclaration =
        from type in TypeKeyword
        from name in Token.EqualTo(BSToken.Identifier)
        from init in (
            from _eq in Token.EqualTo(BSToken.Assign)
            from lit in Literal
            select lit
        ).OptionalOrDefault(null)
        from _semi in Token.EqualTo(BSToken.Semicolon)
        select new VariableDeclaration
        {
            Name = name.ToStringValue(),
            Type = type,
            InitialValueExpression = init?.SourceText,
            DefaultValue = init is BSLiteral bl ? bl.Value : null,
        };

    /// <summary>Parse zero or more variable declarations (ConstBlock/PubVarBlock/##BlockVars body).</summary>
    public static readonly TokenListParser<BSToken, List<VariableDeclaration>> VariableDeclarations =
        VariableDeclaration.Many().Select(arr => new List<VariableDeclaration>(arr)).AtEnd();

    // ─── Public API ─────────────────────────────────────────────────────────

    /// <summary>
    /// Parse a free-form expression string (e.g. a Branch/ForLoop condition text that the
    /// converter carries as a string). Replaces <c>BSExpressionAdapter.Parse</c>. Returns
    /// null when the string is empty or not a single parseable expression.
    /// </summary>
    public static BSExpression? ParseExpression(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        try
        {
            return Expression.Parse(Tokenize(text));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Parse the body of a recognized block into a <see cref="BlockDefinition"/>. Replaces
    /// <c>BlockSyntaxValidator.Validate</c> + <c>BlockStatementExtractor.CreateBlockDefinition</c>.
    /// Diagnostics (parse errors, illegal assignments, nested-call errors) are appended to
    /// <paramref name="diagnostics"/>. The block name, type, line number, and
    /// <see cref="RecognizedBlock.HasExplicitBlockBody"/> flag come from <paramref name="recognized"/>.
    /// </summary>
    /// <param name="registry">Builtin function registry (null → no control-flow dispatch; bare calls only).</param>
    internal static BlockDefinition ParseBlock(
        RecognizedBlock recognized,
        BuiltinFunctionRegistry? registry,
        ConversionDiagnostics diagnostics)
    {
        var block = new BlockDefinition
        {
            Type = recognized.BlockType,
            Name = string.IsNullOrEmpty(recognized.BlockName)
                ? recognized.BlockType.ToString()
                : recognized.BlockName,
            LineNumber = recognized.StartLine,
            HasExplicitBlockBody = recognized.HasExplicitBlockBody,
        };

        // BlockVars sub-section (##BlockVars) — declarations only.
        if (!string.IsNullOrEmpty(recognized.BlockVarsContent))
            block.BlockVars = ParseDeclarations(recognized.BlockVarsContent, recognized, diagnostics);

        // ConstBlock / PubVarBlock — declarations only, no statements.
        if (recognized.BlockType is BlockType.ConstBlock or BlockType.PubVarBlock)
        {
            if (!string.IsNullOrEmpty(recognized.Content))
                block.Variables = ParseDeclarations(recognized.Content, recognized, diagnostics);
            return block;
        }

        // MainBlock / NamedBlock — statements.
        if (!string.IsNullOrEmpty(recognized.Content))
            ParseStatements(recognized, registry, diagnostics, block);

        return block;
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    static List<VariableDeclaration> ParseDeclarations(
        string text, RecognizedBlock recognized, ConversionDiagnostics diagnostics)
    {
        // v5.0 §一: `_` is a reserved placeholder and cannot be used as a variable name.
        // Detect Placeholder token in declaration context (after type keyword, before `;` or `=`).
        TokenList<BSToken> tokens;
        try { tokens = Tokenize(text); } catch { return new List<VariableDeclaration>(); }

        // Walk through tokens looking for TypeKeyword followed by Placeholder.
        var tokenArr = tokens.ToArray();
        for (int i = 0; i < tokenArr.Length - 1; i++)
        {
            if (IsTypeKeyword(tokenArr[i].Kind) && tokenArr[i + 1].Kind == BSToken.Placeholder)
            {
                diagnostics.AddError("BS_RESERVED_PLACEHOLDER",
                    $"[{blockTypeName(recognized)}] `_` is a reserved placeholder and cannot be used as a variable name (§一).",
                    recognized.StartLine);
                return new List<VariableDeclaration>();
            }
        }

        try
        {
            return VariableDeclarations.Parse(tokens);
        }
        catch (ParseException ex)
        {
            diagnostics.AddError("BS_DECL_PARSE",
                $"[{blockTypeName(recognized)}] Declaration parse error: {ex.Message}",
                recognized.StartLine + (ex.ErrorPosition.HasValue ? ex.ErrorPosition.Line - 1 : 0));
            return new List<VariableDeclaration>();
        }
    }

    static bool IsTypeKeyword(BSToken kind) => kind is BSToken.Int or BSToken.Float
        or BSToken.Double or BSToken.Bool or BSToken.StringKW or BSToken.CharKW
        or BSToken.DynamicKW;

    static void ParseStatements(
        RecognizedBlock recognized,
        BuiltinFunctionRegistry? registry,
        ConversionDiagnostics diagnostics,
        BlockDefinition block)
    {
        TokenList<BSToken> tokens;
        try
        {
            tokens = Tokenize(recognized.Content);
        }
        catch (ParseException ex)
        {
            diagnostics.AddError("BS_TOKENIZE",
                $"[{blockTypeName(recognized)}] {ex.Message}",
                recognized.StartLine + (ex.ErrorPosition.HasValue ? ex.ErrorPosition.Line - 1 : 0));
            return;
        }

        // v5.0 §6.3: `=` is globally disabled for assignment — only `>` is allowed.
        // Check tokens for BSToken.Assign (the tokenizer only produces it outside string literals).
        if (tokens.Any(t => t.Kind == BSToken.Assign))
        {
            diagnostics.AddError("BS_ILLEGAL_ASSIGNMENT",
                $"[{blockTypeName(recognized)}] Assignment via `=` is forbidden in v5.0; use the pipeline operator (>) instead (§6.3).",
                recognized.StartLine);
            return;
        }

        List<BSExpression> statements;
        try
        {
            statements = StatementList.Parse(tokens);
        }
        catch (ParseException ex)
        {
            diagnostics.AddError("BS_STMT_PARSE",
                $"[{blockTypeName(recognized)}] {ex.Message}",
                recognized.StartLine + (ex.ErrorPosition.HasValue ? ex.ErrorPosition.Line - 1 : 0));
            return;
        }

        foreach (var parsed in statements)
            DispatchStatement(parsed, recognized, registry, diagnostics, block);
    }

    /// <summary>
    /// v5.0: Builds a <see cref="FlowControlStatement"/> from a <see cref="BSCall"/>
    /// using the declarative <see cref="FlowControlArgLayout"/>. No per-function
    /// hand-written ExtractStatement needed.
    /// </summary>
    static FlowControlStatement BuildFlowControlFromArgLayout(
        IBuiltinFunctionDefinition fcDef, BSCall call, int line)
    {
        var layout = fcDef.ArgLayout!.Value;
        var args = call.Args;
        var stmt = new FlowControlStatement
        {
            LineNumber = line,
            SourceCode = call.SourceText,
            FunctionName = fcDef.FunctionName
        };

        // expressionArgs: leading expression arguments
        int exprCount = layout.ExpressionArgs;
        var flowArgs = new List<string>();
        if (exprCount > 0)
        {
            for (int i = 0; i < exprCount && i < args.Count; i++)
                flowArgs.Add(args[i].SourceText);
            // First expression = condition/selector (if there are also arms)
            if (fcDef.ArmPinNames.Count > 0)
                stmt.ConditionExpression = args[0].SourceText;
        }
        stmt.FlowArguments = flowArgs;

        // blockNameArgs: block-name string literals following expressions
        int blockStart = exprCount;
        int totalBlockNames = args.Count - blockStart;
        var armNames = fcDef.ArmPinNames;

        // Extra block names beyond what fits in arms go to FlowArguments as strings.
        int armCount = layout.BlockNamesVariadic
            ? totalBlockNames
            : armNames.Count;
        int extraBlockNames = totalBlockNames - armCount;

        for (int i = 0; i < extraBlockNames; i++)
            flowArgs.Add(args[blockStart + i].SourceText.Trim('"'));

        // Build arms from the remaining block names
        int armStart = blockStart + extraBlockNames;
        if (layout.BlockNamesVariadic)
        {
            for (int i = armStart; i < args.Count; i++)
            {
                var blockName = ExtractStringArg(args[i]);
                stmt.Arms.Add(new BranchArm
                {
                    PinName = i == armStart ? armNames[0] : (i - armStart - 1).ToString(),
                    TargetBlockName = blockName
                });
            }
        }
        else
        {
            for (int i = 0; i < armNames.Count && (armStart + i) < args.Count; i++)
            {
                var blockName = ExtractStringArg(args[armStart + i]);
                stmt.Arms.Add(new BranchArm { PinName = armNames[i], TargetBlockName = blockName });
            }
        }

        // ForLoop specific: strip quotes from indexName when there are extra block names
        if (fcDef is { HasInternalState: true } && flowArgs.Count >= 4)
            flowArgs[3] = flowArgs[3].Trim('"');

        stmt.FlowArguments = flowArgs;
        return stmt;
    }

    static string ExtractStringArg(BSExpression arg) =>
        (arg is BSLiteral { Kind: BSLiteralKind.String } lit)
            ? lit.Value?.ToString() ?? ""
            : arg.SourceText.Trim('"');

    static void DispatchStatement(
        BSExpression parsed, RecognizedBlock recognized,
        BuiltinFunctionRegistry? registry, ConversionDiagnostics diagnostics,
        BlockDefinition block)
    {
        // Pipeline with targets: always an ExpressionStatement carrying the BSPipeline.
        // A pipeline with zero targets and a single BSCall source is a bare call that the
        // Statement rule happened to wrap — unwrap it so DispatchStatement can consult the
        // builtin registry for control-flow functions (Branch/ForLoop/Switch/Goto/Break).
        if (parsed is BSPipeline pipeline)
        {
            if (pipeline.Targets.Count == 0 && pipeline.Sources.Count == 1
                && pipeline.Sources[0] is BSCall unwrappedCall)
            {
                parsed = unwrappedCall; // fall through to the BSCall branch below
            }
            else
            {
                block.Statements.Add(new ExpressionStatement
                {
                    Expression = pipeline.SourceText,
                    ParsedExpression = pipeline,
                    SourceCode = pipeline.SourceText + ";",
                    LineNumber = recognized.StartLine,
                });
                return;
            }
        }

        // Bare call (no pipeline): could be a flow-control function (Branch/ForLoop/...)
        // dispatched via IFlowControlFunctionDefinition.ArgLayout, or a side-effect call (Print).
        if (parsed is BSCall call)
        {
            var line = recognized.StartLine;

            // v5.0: unified ArgLayout dispatch for flow-control functions.
            // No more per-function hand-written ExtractStatement — the Parser reads
            // IFlowControlFunctionDefinition.ArgLayout and builds FlowControlStatement directly.
            if (registry?.Get(call.MethodName) is IBuiltinFunctionDefinition fcDef && fcDef.ArgLayout != null)
            {
                block.Statements.Add(BuildFlowControlFromArgLayout(fcDef, call, line));
                return;
            }

            // Detect v5.0 illegal nested calls — an arg that is itself a BSCall.
            // The v5.0 grammar forbids this; the user must use the pipeline operator.
            foreach (var arg in call.Args)
            {
                if (arg is BSCall)
                {
                    diagnostics.AddError("BS_NESTED_CALL",
                        $"[{blockTypeName(recognized)}] Nested function calls are forbidden in v5.0; use the pipeline operator (>) instead. " +
                        $"Rewrite '{call.MethodName}({string.Join(", ", call.Args.Select(a => a.SourceText))})' as a pipeline.",
                        line);
                    return;
                }
            }

            block.Statements.Add(new ExpressionStatement
            {
                Expression = call.SourceText,
                ParsedExpression = call,
                SourceCode = call.SourceText + ";",
                LineNumber = line,
            });
            return;
        }

        diagnostics.AddWarning("BS_UNSUPPORTED_EXPR",
            $"[{blockTypeName(recognized)}] Unsupported expression form: {parsed.SourceText}",
            recognized.StartLine);
    }

    static string blockTypeName(RecognizedBlock r) =>
        string.IsNullOrEmpty(r.BlockName) ? r.BlockType.ToString() : r.BlockName;

    static BSExpression BuildCallOrIdentifier((string ShortName, string FullName) name, BSExpression[]? args)
    {
        if (args is null)
        {
            // Bare reference (no parens). Keep as identifier carrying the full dotted path.
            return new BSIdentifier { Name = name.FullName, SourceText = name.FullName };
        }
        var argList = new List<BSExpression>(args);
        return new BSCall
        {
            MethodName = name.ShortName,
            FullMethodName = name.FullName,
            Args = argList,
            RawArgs = argList.Select(a => a.SourceText).ToList(),
            SourceText = $"{name.FullName}({string.Join(", ", argList.Select(a => a.SourceText))})",
        };
    }

    static BSCall BuildTargetCall((string ShortName, string FullName) name, BSExpression[]? args)
    {
        // A pipeline target is always treated as a BSCall — even a bare identifier (variable
        // tap, e.g. `> currentLoop`) becomes a zero-arg BSCall so the pipeline machinery sees
        // a uniform Target shape. Variable-tap detection happens later in PipelineFlattener.
        // SourceText: a bare identifier (no parens) renders as just the name (not "name()")
        // so RenderPipelineSource produces `x > cond` not `x > cond()`.
        if (args is null)
        {
            return new BSCall
            {
                MethodName = name.ShortName,
                FullMethodName = name.FullName,
                Args = new List<BSExpression>(),
                RawArgs = new List<string>(),
                SourceText = name.FullName,
            };
        }
        var argList = new List<BSExpression>(args);
        return new BSCall
        {
            MethodName = name.ShortName,
            FullMethodName = name.FullName,
            Args = argList,
            RawArgs = argList.Select(a => a.SourceText).ToList(),
            SourceText = $"{name.FullName}({string.Join(", ", argList.Select(a => a.SourceText))})",
        };
    }

    static string RenderPipelineText(IReadOnlyList<BSExpression> sources, IReadOnlyList<BSCall> targets)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(string.Join(", ", sources.Select(s => s.SourceText)));
        foreach (var target in targets)
            sb.Append(" > ").Append(target.SourceText);
        return sb.ToString();
    }

    /// <summary>
    /// Decode a C# char literal token (<c>'a'</c>, <c>'\n'</c>, <c>'\\'</c>) to its char value.
    /// Mirrors the Roslyn <c>CharacterLiteralToken</c> value semantics.
    /// </summary>
    static char ParseCharValue(string tokenText)
    {
        // Strip the surrounding single quotes.
        var inner = tokenText.Length >= 2 ? tokenText[1..^1] : tokenText;
        if (inner.Length == 1) return inner[0];
        if (inner.Length == 2 && inner[0] == '\\')
        {
            return inner[1] switch
            {
                'n' => '\n',
                't' => '\t',
                'r' => '\r',
                '\\' => '\\',
                '\'' => '\'',
                '"' => '"',
                '0' => '\0',
                _ => inner[1],
            };
        }
        return inner.Length > 0 ? inner[0] : '\0';
    }
}
