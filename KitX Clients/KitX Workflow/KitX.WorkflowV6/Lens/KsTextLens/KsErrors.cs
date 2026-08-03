namespace KitX.WorkflowV6.Lens.KsTextLens;

/// <summary>
/// KS0xx error-code constants, per KScriptGrammarRule.md §十三. The tokenizer emits
/// KS001–KS006; the parser emits KS010–KS077. Code sites reference these constants
/// (never string literals) so a code rename in the grammar doc needs exactly one edit.
/// </summary>
internal static class KsErrors
{
    /// <summary>KS001 — Tab character is not allowed (any position).</summary>
    public const string TabNotAllowed = "KS001";

    /// <summary>KS002 — Indent must be a multiple of 4.</summary>
    public const string IndentNotMultipleOf4 = "KS002";

    /// <summary>KS003 — Unterminated string literal.</summary>
    public const string UnterminatedStringLiteral = "KS003";

    /// <summary>KS004 — Unterminated char literal.</summary>
    public const string UnterminatedCharLiteral = "KS004";

    /// <summary>KS005 — Unexpected character.</summary>
    public const string UnexpectedCharacter = "KS005";

    /// <summary>KS006 — Malformed numeric literal.</summary>
    public const string MalformedNumericLiteral = "KS006";

    /// <summary>KS010 — Top-level statement must be at indent 0.</summary>
    public const string TopLevelStatementIndent = "KS010";

    /// <summary>KS011 — Duplicate const/var block.</summary>
    public const string DuplicateDeclBlock = "KS011";

    /// <summary>KS012 — Declaration must start with a type name and have a name.</summary>
    public const string InvalidDeclaration = "KS012";

    /// <summary>KS013 — Expected '{' after const/var.</summary>
    public const string ExpectedLBraceAfterConstVar = "KS013";

    /// <summary>KS020 — Expected ':' after case label.</summary>
    public const string ExpectedColonAfterCaseLabel = "KS020";

    /// <summary>KS021 — Expected case label or 'default' in switch arm.</summary>
    public const string ExpectedCaseLabelOrDefault = "KS021";

    /// <summary>KS022 — Duplicate default arm.</summary>
    public const string DuplicateDefaultArm = "KS022";

    /// <summary>KS030 — Expected 'as' after forEach source.</summary>
    public const string ExpectedAsAfterForEach = "KS030";

    /// <summary>KS031 — Expected item name after 'as'.</summary>
    public const string ExpectedItemNameAfterAs = "KS031";

    /// <summary>KS040 — Expected variable name after '='.</summary>
    public const string ExpectedVariableNameAfterAssign = "KS040";

    /// <summary>KS041 — Expected segment name after '>'.</summary>
    public const string ExpectedSegmentNameAfterPipe = "KS041";

    /// <summary>KS042 — Expected ')' to close call arguments.</summary>
    public const string ExpectedRParenToCloseArgs = "KS042";

    /// <summary>KS050 — Unexpected token in expression.</summary>
    public const string UnexpectedTokenInExpression = "KS050";

    /// <summary>KS051 — Function arguments may only be literals or '_' placeholders (v6.0 rule).</summary>
    public const string FunctionArgsOnlyLiteralsOrPlaceholders = "KS051";

    /// <summary>KS052 — Expected ')' to close call arguments.</summary>
    public const string ExpectedRParenToCloseCallArgs = "KS052";

    /// <summary>KS053 — Bare statement is not valid (must have a segment / assignment / call / no-op read).</summary>
    public const string BareStatementInvalid = "KS053";

    /// <summary>KS060 — Multiple sources in condition require a '>' pipeline segment.</summary>
    public const string MultipleSourcesNeedSegment = "KS060";

    /// <summary>KS061 — 'forEach' is not valid in a pipeline/condition context.</summary>
    public const string ForEachInPipeline = "KS061";

    /// <summary>KS062 — Body is empty.</summary>
    public const string EmptyBody = "KS062";

    /// <summary>KS063 — Expected ':' after control-flow header (if/else/while/forEach/switch).</summary>
    public const string ExpectedColonAfterHeader = "KS063";

    /// <summary>KS064 — 'else if' is not supported (bijection guarantee); use 'else:' + nested 'if'.</summary>
    public const string ElseIfUnsupported = "KS064";

    /// <summary>KS065 — Full-line comment between multi-line pipeline continuation lines.</summary>
    public const string CommentBetweenContinuations = "KS065";

    /// <summary>KS066 — Continuation source line indent must be deeper than the statement indent.</summary>
    public const string ContinuationIndentTooShallow = "KS066";

    /// <summary>KS070 — Expected ':' after dict key.</summary>
    public const string ExpectedColonAfterDictKey = "KS070";

    /// <summary>KS071 — Expected ',' or '}' in dict literal.</summary>
    public const string ExpectedCommaOrRBraceInDict = "KS071";

    /// <summary>KS072 — Dict key must be a string literal or identifier.</summary>
    public const string DictKeyMustBeStringOrIdentifier = "KS072";

    /// <summary>KS073 — Nested dict literal is not allowed (use JSON for nested structures).</summary>
    public const string NestedDictLiteralNotAllowed = "KS073";

    /// <summary>KS074 — Dict value must be a scalar literal.</summary>
    public const string DictValueMustBeScalarLiteral = "KS074";

    /// <summary>KS075 — Expected ')' to close parenthesised pipeline source.</summary>
    public const string ExpectedRParenToCloseParenPipeline = "KS075";

    /// <summary>KS076 — Decl-block initialiser must be a single literal; expressions/references banned.</summary>
    public const string DeclInitMustBeSingleLiteral = "KS076";

    /// <summary>KS077 — Dict value must not be a reference/expression.</summary>
    public const string DictValueNoReferences = "KS077";
}
