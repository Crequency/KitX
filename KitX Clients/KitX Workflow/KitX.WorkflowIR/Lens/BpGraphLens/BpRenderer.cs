namespace KitX.Workflow.Lens.BpGraphLens;

using KitX.Core.Contract.Workflow;
using KitX.Workflow.Builtin;
using KitX.Workflow.Ir;

// ─────────────────────────────────────────────────────────────────────────────
// BpRenderer — projects an immutable IrWorkflow into a mutable Blueprint graph.
//
// This is the greenfield §11 implementation (replaces the legacy CFGGraphRenderer).
// Where the legacy renderer rendered CFG → BP directly and carried several known
// smells, this renderer renders IR → BP and addresses each:
//
//   §11.1  each IrBlock → one BlockNode (entry block → EntryNode); title = block
//          name; Comment from IrAnnotation(Comment); standard Exec input pin.
//
//   §11.2  (NEW — legacy deliberately skipped) data-boundary nodes inside each
//          block's sub-graph: ForLoop index → an EntryPointNode; IrBlockVars →
//          inner VariableNodes (VariableKind.BlockVar). The legacy §11.2 note
//          called these "meaningless for BS"; we render them anyway so the BP
//          graph is a faithful projection, even where BS semantics are limited.
//
//   §11.3  control-flow terminator arms → Exec OUTPUT pins on the block node
//          (named per PinName: True/False/LoopBody/LoopEnd/Exec/Default/0..N),
//          each wired to the target block's Exec INPUT pin. A Sequential
//          fall-through edge wires the block's default Exec output to the
//          FallThroughTarget.
//
//   §11.4  data edges from PubVar/GlobalVar-writing pipeline statements → the
//          variable node. FIXES the legacy smell where SourceNodeId was a
//          placeholder empty string: each data edge now has a real source, the
//          statement node that produced the value (rendered inside the block).
//
//   layout coordinates are read from IrAnnotation(Layout): "BlockPos" → the
//          block node's X/Y; <stableId> → per-statement node X/Y. Written to
//          BlueprintNode.Location so the BP view reproduces the saved layout.
//
// Custom node templates: if a function implements IBpRenderHandler, the renderer
// asks it for a BpNodeTemplate (title/ports); otherwise it builds a default
// BuiltinFunctionNode from the function's IBuiltinFunction port spec.
//
// The renderer is a pure read: it never mutates the IR. Contract nodes ARE
// mutable (v4.0 legacy; Phase 10 will move them to records), so producing a
// mutable Blueprint is expected and correct here.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Renders an <see cref="IrWorkflow"/> into a <see cref="Blueprint"/> per §11.
/// Pure read of the IR; produces a mutable Blueprint (Contract nodes are mutable
/// in v4.0). One renderer instance carries the injected builtin registry.
/// </summary>
public sealed class BpRenderer
{
    private readonly BuiltinFunctionRegistry? _registry;

    public BpRenderer() : this(null) { }

    public BpRenderer(BuiltinFunctionRegistry? registry) => _registry = registry;

    /// <summary>Projects <paramref name="ir"/> into a fresh <see cref="Blueprint"/>.</summary>
    public Blueprint Render(IrWorkflow ir)
    {
        var bp = new Blueprint { Name = "Rendered" };

        // Index of layout annotations: block-anchor ("BlockPos") and per-statement
        // (keyed by DeriveStableId), so the BP view reproduces saved coordinates.
        // Built per block on demand.
        var blockNodes = new Dictionary<string, BlueprintNode>();
        // statement-node id, keyed by (blockName, ordinal) so data edges (§11.4) and
        // the reverse translator can locate the producing statement.
        var stmtNodeByOrdinal = new Dictionary<(string Block, int Ordinal), string>();

        // ── Phase 0: PubVar / Const variable nodes (data-edge targets) ──
        // Global vars (PubVars) and constants materialise first so data edges can land.
        var varNodes = new Dictionary<string, VariableNode>();
        foreach (var (name, gvar) in ir.GlobalVars)
        {
            var node = new VariableNode
            {
                VarName = name,
                VarKind = VariableKind.PubVar,
                VarType = gvar.Type.Length > 0 ? gvar.Type : "dynamic",
                Name = name,
                Id = BpNodeIds.PubVar(name),
            };
            bp.AddNode(node);
            varNodes[name] = node;
        }

        foreach (var (name, konst) in ir.Constants)
        {
            var cnode = new ConstNode
            {
                ConstName = name,
                ConstType = konst.Type.Length > 0 ? konst.Type : "string",
                ConstValue = konst.InitialValueExpression ?? konst.DefaultValue?.ToString(),
                Name = name,
                Id = BpNodeIds.Const(name),
            };
            bp.AddNode(cnode);
        }

        // ── Phase 1 (§11.1): one BlockNode per IrBlock, entry → EntryNode ──
        foreach (var block in ir.Blocks)
        {
            BlueprintNode node;
            bool isEntry = block.Kind == IrBlockKind.Entry;
            if (isEntry)
            {
                node = new EntryNode { Name = block.Name, Id = BpNodeIds.Block(block.Name) };
            }
            else
            {
                node = new BlockNode
                {
                    BlockName = block.Name,
                    Name = block.Name,
                    IsMainBlock = false,
                    Id = BpNodeIds.Block(block.Name),
                };
            }

            node.Comment = BlockComment(block);
            ApplyBlockLayout(node, block);
            bp.AddNode(node);
            blockNodes[block.Name] = node;
        }

        // ── Phase 2 (§11.2): inner boundary/variable nodes per block ──
        RenderInnerBoundaryNodes(bp, ir, blockNodes);

        // ── Phase 3: per-statement nodes inside each block + record ordinals ──
        // Each non-control-flow statement becomes a node inside its block (so §11.4
        // data edges have a real source). Control-flow terminators emit NO node —
        // their arms are rendered as Exec output pins on the block node (§11.3).
        // The node is registered in the owning BlockNode's ChildNodeIds so the
        // collapsed/expanded sub-graph (§11.2) can locate its inner nodes.
        RenderStatementNodes(bp, ir, stmtNodeByOrdinal, blockNodes);

        // ── Phase 4 (§11.3): Exec output pins + Exec wires from terminators ──
        RenderExecEdges(bp, ir, blockNodes);

        // ── Phase 5 (§11.4): data edges from PubVar-writing statements ──
        RenderDataEdges(bp, ir, varNodes, stmtNodeByOrdinal);

        // ── Phase 6: BlueprintBlockScope metadata (for reverse conversion) ──
        // Captures block boundaries, fall-through, the BlockVars manifest, and the
        // inner-node id list + owner block-node id (consumed by the Dashboard to
        // rebuild ScopeBlocks and collapse/expand sub-graphs — §11.2).
        RenderBlockScopes(bp, ir, blockNodes);

        // ── Phase 7: Auto-layout ──
        // When no block carries a saved "BlockPos" annotation (i.e. the workflow was
        // never laid out by the user), run the layout engine to produce a readable
        // default arrangement. If any block already has coordinates, the user has
        // edited the layout and we preserve their arrangement.
        if (!HasSavedLayout(ir))
        {
            var layoutService = new LayoutService();
            layoutService.LayoutNodes(bp);
        }

        return bp;
    }

    /// <summary>
    /// Returns true when at least one block in <paramref name="ir"/> carries a
    /// Layout annotation with key "BlockPos" — the signal that the user (or a
    //  prior render) has already placed the block nodes. Used to gate auto-layout.
    /// </summary>
    private static bool HasSavedLayout(IrWorkflow ir)
    {
        foreach (var block in ir.Blocks)
            foreach (var ann in block.Annotations)
                if (ann.IsLayout && ann.Key == "BlockPos")
                    return true;
        return false;
    }

    // ───────────────────────────────────────────────────────────────────────
    // Phase 1 helper: a block's Comment is the IrAnnotation(Comment) value.
    // ───────────────────────────────────────────────────────────────────────
    private static string? BlockComment(IrBlock block)
    {
        foreach (var ann in block.Annotations)
            if (ann.Kind == AnnotationKind.Comment && ann.Value is string s && s.Length > 0)
                return s;
        return null;
    }

    /// <summary>
    /// Reads the block-anchor layout ("BlockPos") and writes it to the node's
    /// <see cref="BlueprintNode.X"/>/<see cref="BlueprintNode.Y"/> (the legacy Contract
    /// exposes X/Y fields, not a Location record).
    /// </summary>
    private static void ApplyBlockLayout(BlueprintNode node, IrBlock block)
    {
        foreach (var ann in block.Annotations)
        {
            if (ann.IsLayout && ann.Key == "BlockPos" && ann.Value is IrLayout layout)
            {
                node.X = layout.X;
                node.Y = layout.Y;
                return;
            }
        }
    }

    // ───────────────────────────────────────────────────────────────────────
    // Phase 2 (§11.2): ForLoop index → EntryPointNode; BlockVars → inner
    // VariableNodes. These live INSIDE the block's sub-graph (their node ids are
    // distinct from the outer block node). The legacy renderer skipped §11.2 as
    // "meaningless for BS"; we render it for a faithful projection.
    // ───────────────────────────────────────────────────────────────────────
    private static void RenderInnerBoundaryNodes(
        Blueprint bp, IrWorkflow ir, IReadOnlyDictionary<string, BlueprintNode> blockNodes)
    {
        foreach (var block in ir.Blocks)
        {
            // Block-local variables → inner VariableNodes (VariableKind.BlockVar).
            foreach (var bv in block.BlockVars)
            {
                var vnode = new VariableNode
                {
                    VarName = bv.Name,
                    VarKind = VariableKind.BlockVar,
                    VarType = bv.Type.Length > 0 ? bv.Type : "dynamic",
                    Name = bv.Name,
                    Id = BpNodeIds.BlockVar(block.Name, bv.Name),
                };
                if (blockNodes.TryGetValue(block.Name, out var owner) && owner is BlockNode bn)
                    bn.ChildNodeIds.Add(vnode.Id);
                bp.AddNode(vnode);
            }

            // ForLoop index variable → EntryPointNode inside the loop body block.
            // The index is injected by the ForLoop node under indexName (§3.4/§7.1);
            // expose it as a data-boundary entry so the body's statements can read it.
            // We detect "this block is a loop body" by scanning predecessors' edges —
            // but in the IR the simplest signal is: the block is referenced as the
            // LoopBody target of some ForLoop terminator. We look that up by name.
            foreach (var pred in ir.Blocks)
            {
                foreach (var stmt in pred.Statements)
                {
                    if (stmt is not IrControlFlowStatement cf) continue;
                    if (cf.Op != ControlFlowOp.ForLoop) continue;
                    var bodyTarget = cf.Targets.FirstOrDefault(t => t.PinName == "LoopBody");
                    if (bodyTarget?.TargetBlockName != block.Name) continue;

                    // indexName is Arguments[3] (from/to/step/indexName/body/end).
                    var indexName = cf.Arguments.Length > 3 ? cf.Arguments[3].Trim('"') : "i";
                    if (indexName.Length == 0) indexName = "i";
                    var ep = new EntryPointNode
                    {
                        PortName = indexName,
                        Name = $"LoopIndex: {indexName}",
                        Id = BpNodeIds.LoopIndex(block.Name, indexName),
                    };
                    if (blockNodes.TryGetValue(block.Name, out var owner) && owner is BlockNode bn)
                        bn.ChildNodeIds.Add(ep.Id);
                    bp.AddNode(ep);
                }
            }
        }
    }

    // ───────────────────────────────────────────────────────────────────────
    // Phase 3: render one node per non-control-flow statement inside each block.
    // This is what lets §11.4 data edges point at a real source node (the fix for
    // the legacy SourceNodeId = string.Empty hack). Control-flow terminators are
    // skipped here — they are represented structurally as the block's Exec arms.
    // ───────────────────────────────────────────────────────────────────────
    private void RenderStatementNodes(
        Blueprint bp,
        IrWorkflow ir,
        Dictionary<(string Block, int Ordinal), string> stmtNodeByOrdinal,
        IReadOnlyDictionary<string, BlueprintNode> blockNodes)
    {
        foreach (var block in ir.Blocks)
        {
            int ordinal = 0;
            foreach (var stmt in block.Statements)
            {
                // Control-flow terminators emit no standalone node (§11.3 rule).
                if (stmt is IrControlFlowStatement)
                {
                    ordinal++;
                    continue;
                }

                if (stmt is not IrPipelineStatement pipe)
                {
                    ordinal++;
                    continue;
                }

                var id = BpNodeIds.Statement(block.Name, stmt.Fingerprint, ordinal);
                var node = BuildStatementNode(pipe);
                node.Id = id;
                node.Comment = stmt.Comment;
                ApplyStatementLayout(node, block, stmt.Fingerprint);
                bp.AddNode(node);

                // §11.2: register the statement node in its owning BlockNode's
                // ChildNodeIds so the collapsed/expanded sub-graph can find it.
                if (blockNodes.TryGetValue(block.Name, out var owner) && owner is BlockNode bn)
                    bn.ChildNodeIds.Add(node.Id);

                stmtNodeByOrdinal[(block.Name, ordinal)] = id;
                ordinal++;
            }
        }
    }

    /// <summary>
    /// Builds a BuiltinFunctionNode for a pipeline statement. If the call's
    /// function implements <see cref="IBpRenderHandler"/>, use its custom
    /// <see cref="BpNodeTemplate"/>; otherwise fall back to the registry's port
    /// spec (or a generic node when the function is unknown).
    /// </summary>
    private BuiltinFunctionNode BuildStatementNode(IrPipelineStatement pipe)
    {
        // Resolve the function name from the FIRST FunctionCall segment (the head
        // of the pipeline). A pipeline may have several segments; the head is the
        // node's identity for BP colouring / ports.
        var head = pipe.Segments.FirstOrDefault(s => s.Kind == IrSegmentKind.FunctionCall);
        var funcName = head?.FunctionName ?? "Pipeline";

        var node = new BuiltinFunctionNode { FunctionName = funcName, Name = funcName };

        // Custom template via IBpRenderHandler, if the function declares one.
        if (_registry is not null && funcName.Length > 0)
        {
            if (_registry.GetBpRenderer(funcName) is { } renderer)
            {
                var template = renderer.RenderToBp(pipe);
                node.SetDescriptor(new NodeDescriptor(
                    InputPins: ToPinDescriptors(template.Inputs),
                    OutputPins: ToPinDescriptors(template.Outputs),
                    DisplayName: template.Title));
                node.Name = template.Title;
                // Re-materialise the pins from the descriptor (BuiltinFunctionNode's
                // constructor does not call InitializePinsFromDescriptor; we do it here).
                node.InputPins.Clear();
                node.OutputPins.Clear();
                foreach (var pd in node.GetDescriptor().InputPins)
                    node.InputPins.Add(new BlueprintPin { Name = pd.Name, Direction = PinDirection.Input, Type = pd.Type });
                foreach (var pd in node.GetDescriptor().OutputPins)
                    node.OutputPins.Add(new BlueprintPin { Name = pd.Name, Direction = PinDirection.Output, Type = pd.Type });
                return node;
            }

            // Default: build from the function's IBuiltinFunction port spec.
            if (_registry.Get(funcName) is { } fn)
            {
                var inputs = fn.InputPorts.Select(p => new PinDescriptor(p.Name, p.Type, p.RelativeY)).ToList();
                var outputs = fn.OutputPorts.Select(p => new PinDescriptor(p.Name, p.Type, p.RelativeY)).ToList();
                node.SetDescriptor(new NodeDescriptor(inputs, outputs, fn.Name));
                node.InputPins.Clear();
                node.OutputPins.Clear();
                foreach (var pd in inputs)
                    node.InputPins.Add(new BlueprintPin { Name = pd.Name, Direction = PinDirection.Input, Type = pd.Type });
                foreach (var pd in outputs)
                    node.OutputPins.Add(new BlueprintPin { Name = pd.Name, Direction = PinDirection.Output, Type = pd.Type });
                return node;
            }
        }

        // Unknown function / no registry: a generic node with one Any input + one Any output.
        node.SetDescriptor(new NodeDescriptor(
            InputPins: [new PinDescriptor("In", PinType.Any, 30)],
            OutputPins: [new PinDescriptor("Out", PinType.Any, 70)],
            DisplayName: funcName));
        node.InputPins.Clear();
        node.OutputPins.Clear();
        node.InputPins.Add(new BlueprintPin { Name = "In", Direction = PinDirection.Input, Type = PinType.Any });
        node.OutputPins.Add(new BlueprintPin { Name = "Out", Direction = PinDirection.Output, Type = PinType.Any });
        return node;
    }

    private static List<PinDescriptor> ToPinDescriptors(IReadOnlyList<PortSpec> ports) =>
        ports.Select(p => new PinDescriptor(p.Name, p.Type, p.RelativeY)).ToList();

    /// <summary>
    /// Reads a per-statement layout annotation (keyed by the statement's fingerprint
    /// string) and applies it to the node's X/Y. The fingerprint-keyed annotation is
    /// the per-node layout channel (the block anchor uses "BlockPos" instead).
    /// </summary>
    private static void ApplyStatementLayout(BlueprintNode node, IrBlock block, IrFingerprint fp)
    {
        var key = fp.Value;
        foreach (var ann in block.Annotations)
        {
            if (ann.IsLayout && ann.Key == key && ann.Value is IrLayout layout)
            {
                node.X = layout.X;
                node.Y = layout.Y;
                return;
            }
        }
    }

    // ───────────────────────────────────────────────────────────────────────
    // Phase 4 (§11.3): control-flow terminator arms → Exec OUTPUT pins on the
    // block node, wired to the target block's Exec INPUT pin. A Sequential
    // fall-through edge wires the block's default Exec output to the
    // FallThroughTarget. Break/Exit emit no arms.
    // ───────────────────────────────────────────────────────────────────────
    private static void RenderExecEdges(
        Blueprint bp, IrWorkflow ir, IReadOnlyDictionary<string, BlueprintNode> blockNodes)
    {
        foreach (var block in ir.Blocks)
        {
            if (!blockNodes.TryGetValue(block.Name, out var node)) continue;

            var terminator = GetTerminator(block);
            if (terminator is not null)
            {
                // One Exec output pin per terminator arm (True/False/LoopBody/LoopEnd/Exec/Default/0..N).
                foreach (var target in terminator.Targets)
                {
                    // De-dupe by pin name: a Switch template's Default + 0..N are unique,
                    // but we guard against a malformed IR with a repeated pin name.
                    if (node.OutputPins.Any(p => p.Name == target.PinName && p.Type == PinType.Execution))
                        continue;
                    node.OutputPins.Add(new BlueprintPin
                    {
                        Name = target.PinName,
                        Direction = PinDirection.Output,
                        Type = PinType.Execution,
                    });
                }

                // Wire each arm to its target block's Exec input pin.
                foreach (var target in terminator.Targets)
                {
                    if (string.IsNullOrEmpty(target.TargetBlockName)) continue;
                    if (!blockNodes.TryGetValue(target.TargetBlockName, out var targetNode)) continue;
                    var srcPin = node.OutputPins.FirstOrDefault(p => p.Name == target.PinName && p.Type == PinType.Execution);
                    var tgtPin = targetNode.InputPins.FirstOrDefault(p => p.Type == PinType.Execution);
                    if (srcPin is null || tgtPin is null) continue;
                    bp.AddConnection(new BlueprintConnection
                    {
                        SourceNodeId = node.Id,
                        SourcePinId = srcPin.Id,
                        TargetNodeId = targetNode.Id,
                        TargetPinId = tgtPin.Id,
                    });
                }
            }
            else
            {
                // Sequential fall-through: wire the block's default Exec output to
                // the FallThroughTarget (the ToBlockName of the Sequential edge).
                var fallThrough = block.FallThroughTarget;
                if (!string.IsNullOrEmpty(fallThrough) &&
                    blockNodes.TryGetValue(fallThrough!, out var targetNode))
                {
                    var srcPin = node.OutputPins.FirstOrDefault(p => p.Type == PinType.Execution);
                    var tgtPin = targetNode.InputPins.FirstOrDefault(p => p.Type == PinType.Execution);
                    if (srcPin is not null && tgtPin is not null)
                    {
                        bp.AddConnection(new BlueprintConnection
                        {
                            SourceNodeId = node.Id,
                            SourcePinId = srcPin.Id,
                            TargetNodeId = targetNode.Id,
                            TargetPinId = tgtPin.Id,
                        });
                    }
                }
            }
        }
    }

    /// <summary>If the block ends with a control-flow terminator, return it; else null.</summary>
    private static IrControlFlowStatement? GetTerminator(IrBlock block)
    {
        if (block.Statements.Length == 0) return null;
        return block.Statements[^1] as IrControlFlowStatement;
    }

    // ───────────────────────────────────────────────────────────────────────
    // Phase 5 (§11.4): data edges from PubVar/GlobalVar-writing pipeline
    // statements → the variable node. FIXES the legacy smell where SourceNodeId
    // was a placeholder empty string: each data edge now points at the real
    // statement node that produced the value (rendered in Phase 3).
    // ───────────────────────────────────────────────────────────────────────
    private static void RenderDataEdges(
        Blueprint bp,
        IrWorkflow ir,
        IReadOnlyDictionary<string, VariableNode> varNodes,
        IReadOnlyDictionary<(string Block, int Ordinal), string> stmtNodeByOrdinal)
    {
        // §2.2 fix: track (sourceId, targetId, pubVar) triples already drawn so the
        // consumption scan below does not duplicate a wire the tap loop drew.
        var drawn = new HashSet<(string Src, string Tgt, string Var)>();

        foreach (var block in ir.Blocks)
        {
            int ordinal = 0;
            foreach (var stmt in block.Statements)
            {
                if (stmt is IrPipelineStatement pipe)
                {
                    stmtNodeByOrdinal.TryGetValue((block.Name, ordinal), out var stmtId);

                    // (a) Output tap: the terminal Variable segment (if any) is the
                    // assignment target — draw a wire stmt → PubVar.
                    var tap = pipe.Segments.LastOrDefault(s => s.Kind == IrSegmentKind.Variable);
                    if (tap?.VariableName is { Length: > 0 } varName
                        && varNodes.TryGetValue(varName, out var targetVar))
                    {
                        bp.Connections.Add(new BlueprintConnection
                        {
                            SourceNodeId = stmtId ?? string.Empty,
                            TargetNodeId = targetVar.Id,
                            PubVarName = varName,
                        });
                        drawn.Add((stmtId ?? string.Empty, targetVar.Id, varName));
                    }

                    // (b) Consumption: any head FunctionCall argument whose literal
                    // names a known PubVar draws the reverse wire PubVar → stmt. This
                    // makes the data wire a user dragged from a variable node to an
                    // input pin visible on re-render (§2.2).
                    var head = pipe.Segments.FirstOrDefault(s => s.Kind == IrSegmentKind.FunctionCall);
                    if (head is not null && stmtId is { Length: > 0 })
                    {
                        foreach (var arg in head.Arguments)
                        {
                            var lit = arg.Literal;
                            if (lit is null or { Length: 0 }) continue;
                            // The arg literal may be a bare var name or a quoted string.
                            // Only a bare identifier matching a PubVar is a consumption wire.
                            if (varNodes.TryGetValue(lit, out var srcVar))
                            {
                                var key = (srcVar.Id, stmtId, lit);
                                if (drawn.Add(key))
                                {
                                    bp.Connections.Add(new BlueprintConnection
                                    {
                                        SourceNodeId = srcVar.Id,
                                        TargetNodeId = stmtId,
                                        PubVarName = lit,
                                    });
                                }
                            }
                        }
                    }
                }
                ordinal++;
            }
        }
    }

    // ───────────────────────────────────────────────────────────────────────
    // Phase 6: BlueprintBlockScope metadata (for reverse conversion). Captures
    // block boundaries, fall-through, and the BlockVars manifest.
    // ───────────────────────────────────────────────────────────────────────
    private static void RenderBlockScopes(
        Blueprint bp,
        IrWorkflow ir,
        IReadOnlyDictionary<string, BlueprintNode> blockNodes)
    {
        foreach (var block in ir.Blocks)
        {
            var scope = new BlueprintBlockScope
            {
                Name = block.Name,
                IsMainBlock = block.Kind == IrBlockKind.Entry,
                NextBlockName = block.FallThroughTarget,
                HasExplicitBlockBody = block.HasExplicitBlockBody,
            };
            foreach (var bv in block.BlockVars)
                scope.BlockVars.Add(new BlockVarEntry(bv.Name, bv.Type, bv.InitialValueExpression));

            // §11.2: populate the inner-node id list and owner block-node id so
            // the Dashboard can rebuild ScopeBlocks and collapse/expand sub-graphs.
            // The ChildNodeIds were filled in Phase 2 (boundary nodes) and Phase 3
            // (statement nodes); here we surface them into the BlockScope metadata.
            if (blockNodes.TryGetValue(block.Name, out var node))
            {
                scope.OwnerNodeId = node.Id;
                if (node is BlockNode bn)
                {
                    foreach (var childId in bn.ChildNodeIds)
                        scope.NodeIds.Add(childId);
                }
            }

            bp.BlockScopes.Add(scope);
        }
    }
}
