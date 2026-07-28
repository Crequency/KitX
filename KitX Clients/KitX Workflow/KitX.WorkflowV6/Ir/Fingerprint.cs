using System.Security.Cryptography;
using System.Text;
using KitX.WorkflowV6.Ir.Ast;
using KitX.WorkflowV6.Ir.Statements;

namespace KitX.WorkflowV6.Ir;

// ─────────────────────────────────────────────────────────────────────────────
// Fingerprint — content-derived stable identity for IR statements.
//
// Inherited concept from KitX.WorkflowIR's IrFingerprint: identity is derived from
// semantic content, not from a random Guid. This makes re-parsing the same KS text
// produce the same identities, which is the precondition for diff alignment and
// stable BP node correlation.
//
// The v6 IR is structured (nested AST, not block + Goto). Fingerprint scope therefore
// follows the lexical path of the statement (parent block path + ordinal), not the
// v5 (blockName, ordinal) pair. The DeriveStableId signature below reflects that.
//
// The <see cref="Compute(Statement)"/> algorithm walks the structured Statement tree
// (depth-first) and folds each node's kind + content fields + child fingerprints into
// a SHA-256. The fingerprint is therefore:
//   • re-parse-stable   — same content → same fingerprint, across re-parse
//   • structure-aware   — two ifs with equal condition but different bodies differ
//   • whitespace-robust — content is compared as the structured AST, not as text, so
//                          formatting drift never changes identity
//
// <see cref="Compute(KsNode)"/> does the same for KS AST nodes (used while lowering,
// so a statement's IR fingerprint can be derived from its pre-lowered AST form and
// match the post-lowering IR fingerprint).
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// A content-derived, re-parse-stable identity for a structured IR statement or a KS
/// AST node. Equality is string equality on <see cref="Value"/>.
/// </summary>
public readonly record struct Fingerprint(string Value) : IEquatable<Fingerprint>
{
    public override string ToString() => Value;

    // ── IR-level fingerprinting ──

    /// <summary>
    /// Computes the structural fingerprint of a <see cref="Statement"/> by a depth-first
    /// walk of its subtree. The fingerprint folds in:
    ///   • the statement's <see cref="StatementKind"/> (so a Pipeline and an If never collide)
    ///   • its content fields (condition KsNode, sources, target names, item name, ...)
    ///   • the fingerprints of its child statements (then/else bodies, loop body, arms, ...)
    /// so two statements with the same kind + content + children have the same fingerprint,
    /// and any structural difference makes them differ.
    /// </summary>
    public static Fingerprint Compute(Statement stmt)
    {
        ArgumentNullException.ThrowIfNull(stmt);
        var accum = new HashAccum();
        accum.AddKind(stmt.Kind);
        accum.AddOptional(stmt.LeadingComment);
        accum.AddOptional(stmt.TrailingComment);
        // SourceLine deliberately excluded: it's source-location metadata, not
        // semantic content. Two statements with the same content but on different
        // lines (e.g. after re-formatting) must produce the same fingerprint.

        switch (stmt)
        {
            case PipelineStatement p:
                accum.AddInt(p.Sources.Length);
                foreach (var src in p.Sources) accum.AddKsNode(src);
                accum.AddInt(p.Segments.Length);
                foreach (var seg in p.Segments)
                {
                    accum.AddString(seg.Target);
                    // IsVariableTap deliberately NOT hashed: see Segment.Equals rationale
                    // (the flag is derived from Arguments.Length + registry membership, and
                    // the forward/reverse paths set it asymmetrically for the `> name` form).
                    accum.AddOptional(seg.Comment);
                    accum.AddInt(seg.Arguments.Length);
                    foreach (var arg in seg.Arguments) accum.AddKsNode(arg);
                }
                break;

            case IfStatement iff:
                accum.AddKsNode(iff.Condition);
                accum.AddChildFingerprints(iff.ThenBody);
                accum.AddChildFingerprints(iff.ElseBody);
                break;

            case SwitchStatement sw:
                accum.AddKsNode(sw.Selector);
                accum.AddInt(sw.Arms.Length);
                foreach (var label in sw.ArmLabels) accum.AddInt(label);
                foreach (var arm in sw.Arms) accum.AddChildFingerprints(arm);
                accum.AddChildFingerprints(sw.Default);
                break;

            case ForEachStatement fe:
                accum.AddKsNode(fe.Source);
                accum.AddString(fe.ItemName);
                accum.AddChildFingerprints(fe.Body);
                break;

            case WhileStatement ws:
                accum.AddKsNode(ws.Condition);
                accum.AddChildFingerprints(ws.Body);
                break;

            case BreakStatement br:
                accum.AddOptional(br.Label);
                break;

            case ContinueStatement co:
                accum.AddOptional(co.Label);
                break;

            default:
                // Unknown statement kind: fall back to the runtime type name so a future
                // statement kind never silently collides with an existing one.
                accum.AddString(stmt.GetType().FullName ?? stmt.GetType().Name);
                break;
        }

        return new Fingerprint(accum.ToHex());
    }

    // ── KS AST-level fingerprinting ──

    /// <summary>
    /// Computes the structural fingerprint of a <see cref="KsNode"/> — used during
    /// lowering so a statement's IR fingerprint can be derived from its pre-lowered AST
    /// form and match the post-lowering IR fingerprint.
    /// </summary>
    public static Fingerprint Compute(KsNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        var accum = new HashAccum();
        accum.AddString(node.GetType().Name);
        // SourceLine deliberately excluded: not semantic content (see Compute(Statement)).
        AccumulateKsNode(accum, node);
        return new Fingerprint(accum.ToHex());
    }

    private static void AccumulateKsNode(HashAccum accum, KsNode node)
    {
        // Statement-level comments participate in the AST fingerprint so comment
        // changes are detectable as identity changes (mirrors the IR fingerprint).
        if (node is KsStatement ksStmt)
        {
            accum.AddOptional(ksStmt.LeadingComment);
            accum.AddOptional(ksStmt.TrailingComment);
        }
        switch (node)
        {
            case KsLiteral lit:
                accum.AddString(lit.Kind.ToString());
                accum.AddOptional(lit.Value?.ToString());
                break;
            case KsIdentifier id:
                accum.AddString(id.Name);
                break;
            case KsCall call:
                accum.AddString(call.MethodName);
                accum.AddString(call.FullMethodName);
                accum.AddInt(call.Args.Length);
                foreach (var a in call.Args) accum.AddKsNode(a);
                break;
            case KsPipeline pipe:
                accum.AddInt(pipe.Sources.Length);
                foreach (var s in pipe.Sources) accum.AddKsNode(s);
                accum.AddInt(pipe.Segments.Length);
                foreach (var seg in pipe.Segments)
                {
                    accum.AddString(seg.Target);
                    accum.AddBool(seg.IsVariableTap);
                    accum.AddOptional(seg.Comment);
                    accum.AddInt(seg.Args.Length);
                    foreach (var a in seg.Args) accum.AddKsNode(a);
                }
                break;
            case KsPipelineSegment seg:
                accum.AddString(seg.Target);
                accum.AddBool(seg.IsVariableTap);
                accum.AddOptional(seg.Comment);
                accum.AddInt(seg.Args.Length);
                foreach (var a in seg.Args) accum.AddKsNode(a);
                break;
            case KsPlaceholder:
                break;
            case KsConstDecl cd:
                accum.AddString(cd.Name);
                accum.AddString(cd.Type);
                accum.AddOptional(cd.InitialValueExpression);
                break;
            case KsVarDecl vd:
                accum.AddString(vd.Name);
                accum.AddString(vd.Type);
                accum.AddOptional(vd.InitialValueExpression);
                break;
            case KsConstBlock cb:
                accum.AddInt(cb.Declarations.Length);
                foreach (var d in cb.Declarations) accum.AddKsNode(d);
                break;
            case KsVarBlock vb:
                accum.AddInt(vb.Declarations.Length);
                foreach (var d in vb.Declarations) accum.AddKsNode(d);
                break;
            case KsIf iff:
                accum.AddKsNode(iff.Condition);
                accum.AddChildAstFingerprints(iff.ThenBody);
                accum.AddChildAstFingerprints(iff.ElseBody);
                break;
            case KsSwitch sw:
                accum.AddKsNode(sw.Selector);
                accum.AddInt(sw.Arms.Length);
                foreach (var label in sw.ArmLabels) accum.AddInt(label);
                foreach (var arm in sw.Arms) accum.AddChildAstFingerprints(arm);
                accum.AddChildAstFingerprints(sw.Default);
                break;
            case KsForEach fe:
                accum.AddKsNode(fe.Source);
                accum.AddString(fe.ItemName);
                accum.AddChildAstFingerprints(fe.Body);
                break;
            case KsWhile ws:
                accum.AddKsNode(ws.Condition);
                accum.AddChildAstFingerprints(ws.Body);
                break;
            case KsBreak:
            case KsContinue:
                break;
            case KsProgram prog:
                accum.AddBool(prog.ConstBlock is not null);
                if (prog.ConstBlock is not null) accum.AddKsNode(prog.ConstBlock);
                accum.AddBool(prog.VarBlock is not null);
                if (prog.VarBlock is not null) accum.AddKsNode(prog.VarBlock);
                accum.AddChildAstFingerprints(prog.Body);
                break;
            default:
                accum.AddString(node.GetType().FullName ?? node.GetType().Name);
                break;
        }
    }

    // ── Legacy textual-form entry (kept for backwards-compat with v5 callers) ──

    /// <summary>
    /// Computes a fingerprint from a raw textual form. Kept as a convenience for tests
    /// and for the migration path; prefer <see cref="Compute(Statement)"/> /
    /// <see cref="Compute(KsNode)"/> for real IR/AST fingerprinting.
    /// </summary>
    public static Fingerprint Compute(string textualForm)
        => new(textualForm ?? string.Empty);

    /// <summary>
    /// Derives a short, stable correlation id for a statement living at
    /// <paramref name="lexicalPath"/> (a "/"-separated scope path inside the
    /// structured AST) at <paramref name="ordinal"/>. Used as the BP-node-id and
    /// the per-node-layout key. Stable across KS re-parse because it only depends
    /// on (lexical path, statement fingerprint, in-scope position).
    /// </summary>
    [Obsolete("Replaced by KitX.WorkflowV6.Ir.NodeId.Of. The debug codegen now derives " +
              "statementId via FNV-1a over the lexical path alone, identical to BpRenderer's " +
              "BP-node id. This keeps statementId == nodeId so breakpoints set on a BP node " +
              "fire when execution reaches the matching IR statement. Scheduled for removal.")]
    public static string DeriveStableId(string lexicalPath, Fingerprint fingerprint, int ordinal)
    {
        var raw = $"{lexicalPath}\u001F{fingerprint.Value}\u001F{ordinal}";
        var bytes = Encoding.UTF8.GetBytes(raw);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash, 0, 6); // 12 hex chars
    }

    // ── Hash accumulator helper ──

    /// <summary>
    /// Internal incremental hash accumulator. Wraps a SHA-256 builder and provides
    /// typed Add helpers (string / int / bool / KsNode / child Statement fingerprints)
    /// so the fingerprint algorithm above reads as a flat list of contributions.
    /// </summary>
    private sealed class HashAccum
    {
        private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        public void AddKind(StatementKind kind)
        {
            _hash.AppendData(Encoding.UTF8.GetBytes(kind.ToString()));
            _hash.AppendData([(byte)':']);
        }

        public void AddString(string s)
        {
            _hash.AppendData(Encoding.UTF8.GetBytes(s));
            _hash.AppendData([(byte)',']);
        }

        public void AddOptional(string? s)
        {
            if (s is null)
            {
                _hash.AppendData([(byte)'?']);
                return;
            }
            _hash.AppendData([(byte)'!']);
            _hash.AppendData(Encoding.UTF8.GetBytes(s));
            _hash.AppendData([(byte)',']);
        }

        public void AddInt(int i)
        {
            _hash.AppendData(BitConverter.GetBytes(i));
            _hash.AppendData([(byte)'#']);
        }

        public void AddBool(bool b)
        {
            _hash.AppendData([(byte)(b ? (byte)'T' : (byte)'F')]);
        }

        public void AddKsNode(KsNode node)
        {
            // Recurse: a nested AST node's full structural fingerprint folds into the parent.
            var sub = Compute(node);
            _hash.AppendData(Encoding.UTF8.GetBytes(sub.Value));
            _hash.AppendData([(byte)';']);
        }

        public void AddChildFingerprints(ImmutableArray<Statement> children)
        {
            AddInt(children.Length);
            foreach (var c in children)
            {
                var sub = Compute(c);
                _hash.AppendData(Encoding.UTF8.GetBytes(sub.Value));
                _hash.AppendData([(byte)';']);
            }
        }

        public void AddChildAstFingerprints(IReadOnlyList<KsStatement> children)
        {
            AddInt(children.Count);
            foreach (var c in children)
            {
                var sub = Compute(c);
                _hash.AppendData(Encoding.UTF8.GetBytes(sub.Value));
                _hash.AppendData([(byte)';']);
            }
        }

        public string ToHex()
        {
            var bytes = _hash.GetHashAndReset();
            return Convert.ToHexString(bytes);
        }
    }
}