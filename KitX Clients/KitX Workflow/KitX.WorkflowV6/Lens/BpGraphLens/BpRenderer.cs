namespace KitX.WorkflowV6.Lens.BpGraphLens;

using KitX.Core.Contract.Workflow;
using KitX.WorkflowV6.Builtin;
using KitX.WorkflowV6.Ir;
using KitX.WorkflowV6.Ir.Ast;
using KitX.WorkflowV6.Ir.Statements;

// ─────────────────────────────────────────────────────────────────────────────
// BpRenderer — structured IR → Blueprint graph data.
// ─────────────────────────────────────────────────────────────────────────────

internal sealed class BpRenderer
{
    private readonly BuiltinFunctionRegistry _registry;
    private readonly ILayoutService _layout;
    private Blueprint _bp = null!;

    public BpRenderer(BuiltinFunctionRegistry registry, ILayoutService? layout = null)
    {
        _registry = registry;
        _layout = layout ?? new LayoutService();
    }

    public Blueprint Render(Workflow ir)
    {
        _bp = new Blueprint { Name = "Workflow" };
        RenderScope(ir.Body, "/top", null);
        _layout.Layout(_bp);
        return _bp;
    }

    private void RenderScope(ImmutableArray<Statement> body, string scopePath, BlueprintNode? parentEntry)
    {
        if (body.IsEmpty && parentEntry is null) return;
        var entry = Add(new EntryNode { Name = "Start" }, $"{scopePath}/entry");
        if (parentEntry is not null) ConnectExec(parentEntry, entry);
        BlueprintNode? last = entry;
        for (int i = 0; i < body.Length; i++)
            last = RenderStatement(body[i], $"{scopePath}/stmt/{i}", last);
        var exit = Add(new ExitPointNode { Name = "End" }, $"{scopePath}/exit");
        if (last is not null) ConnectExec(last, exit);
    }

    private BlueprintNode? RenderStatement(Statement stmt, string path, BlueprintNode? prev)
    {
        return stmt switch
        {
            PipelineStatement p => RenderPipeline(p, path),
            IfStatement iff => RenderIfElse(iff, path),
            ForEachStatement fe => RenderForEach(fe, path),
            WhileStatement ws => RenderWhile(ws, path),
            SwitchStatement sw => RenderSwitch(sw, path),
            BreakStatement => AddCtrlNode("break", path),
            ContinueStatement => AddCtrlNode("continue", path),
            ExitStatement => AddCtrlNode("exit", path),
            _ => prev,
        };
    }

    private BuiltinFunctionNode RenderPipeline(PipelineStatement p, string path)
    {
        string fn = "?"; ImmutableArray<BsNode> args = [];
        if (p.Sources.Length == 1 && p.Sources[0] is BsCall c) { fn = c.MethodName; args = c.Args; }
        else if (p.Segments.Length > 0) { var s = p.Segments[^1]; fn = s.Target; args = s.Arguments; }
        var func = AddBuiltin(fn, path);
        if (args.Length > 0) WireFuncArgs(func, args, $"{path}/args");
        if (p.Segments.Length > 0 && p.Segments[^1].IsVariableTap)
        {
            var tap = Add(new VariableNode { Name = p.Segments[^1].Target, VarName = p.Segments[^1].Target }, $"{path}/tap");
            ConnectValue(func, tap);
        }
        return func;
    }

    private BuiltinFunctionNode RenderIfElse(IfStatement iff, string path)
    {
        var br = AddBranch("Branch", path);
        br.OutputPins.Add(MakePin("True", PinDirection.Output));
        br.OutputPins.Add(MakePin("False", PinDirection.Output));
        var thenE = Add(new EntryNode { Name = "Start" }, $"{path}/then"); ConnectNamedExec(br, "True", thenE);
        RenderScope(iff.ThenBody, $"{path}/then", thenE);
        if (iff.ElseBody.Length > 0)
        {
            var elseE = Add(new EntryNode { Name = "Start" }, $"{path}/else"); ConnectNamedExec(br, "False", elseE);
            RenderScope(iff.ElseBody, $"{path}/else", elseE);
        }
        return br;
    }

    private BuiltinFunctionNode RenderForEach(ForEachStatement fe, string path)
    {
        var each = AddLoop("Each", path);
        each.OutputPins.Add(MakePin("Body", PinDirection.Output));
        each.OutputPins.Add(MakePin("End", PinDirection.Output));
        each.OutputPins.Add(MakePin("Current", PinDirection.Output));
        var bodyE = Add(new EntryNode { Name = "Start" }, $"{path}/body"); ConnectNamedExec(each, "Body", bodyE);
        RenderScope(fe.Body, $"{path}/body", bodyE);
        return each;
    }

    private BuiltinFunctionNode RenderWhile(WhileStatement ws, string path)
    {
        var wh = AddLoop("While", path);
        wh.OutputPins.Add(MakePin("Body", PinDirection.Output));
        wh.OutputPins.Add(MakePin("End", PinDirection.Output));
        var bodyE = Add(new EntryNode { Name = "Start" }, $"{path}/body"); ConnectNamedExec(wh, "Body", bodyE);
        RenderScope(ws.Body, $"{path}/body", bodyE);
        return wh;
    }

    private BuiltinFunctionNode RenderSwitch(SwitchStatement sw, string path)
    {
        var sn = AddBranch("Switch", path);
        for (int i = 0; i < sw.Arms.Length; i++)
        {
            sn.OutputPins.Add(MakePin($"{i}", PinDirection.Output));
            var ae = Add(new EntryNode { Name = "Start" }, $"{path}/arm/{i}"); ConnectNamedExec(sn, $"{i}", ae);
            RenderScope(sw.Arms[i], $"{path}/arm/{i}", ae);
        }
        if (sw.Default.Length > 0)
        {
            sn.OutputPins.Add(MakePin("Default", PinDirection.Output));
            var de = Add(new EntryNode { Name = "Start" }, $"{path}/default"); ConnectNamedExec(sn, "Default", de);
            RenderScope(sw.Default, $"{path}/default", de);
        }
        return sn;
    }

    private void WireFuncArgs(BuiltinFunctionNode func, ImmutableArray<BsNode> sources, string path)
    {
        for (int i = 0; i < sources.Length; i++)
        {
            switch (sources[i])
            {
                case BsLiteral lit:
                    var cn = Add(new ConstNode { Name = lit.Value?.ToString() ?? "null", ConstName = lit.Value?.ToString() ?? "null", ConstValue = lit.Value?.ToString() }, $"{path}/{i}");
                    ConnectValue(cn, func);
                    break;
                case BsIdentifier id:
                    var vn = Add(new VariableNode { Name = id.Name, VarName = id.Name }, $"{path}/{i}");
                    ConnectValue(vn, func);
                    break;
                case BsCall nested:
                    var nf = AddBuiltin(nested.MethodName, $"{path}/{i}");
                    WireFuncArgs(nf, nested.Args, $"{path}/{i}/args");
                    ConnectValue(nf, func);
                    break;
            }
        }
    }

    // ── Node factories ──

    private BuiltinFunctionNode AddBuiltin(string name, string path)
    {
        var n = new BuiltinFunctionNode { Name = name, FunctionName = name };
        n.InputPins.Add(MakePin("Exec", PinDirection.Input));
        n.OutputPins.Add(MakePin("Exec", PinDirection.Output));
        var bi = _registry.Get(name);
        if (bi?.Kind is FunctionKind.Pure or FunctionKind.SideEffect)
            n.InputPins.Add(MakePin("Value", PinDirection.Input));
        if (bi?.Kind == FunctionKind.Pure)
            n.OutputPins.Add(MakePin("Value", PinDirection.Output));
        return Add(n, path);
    }

    private BuiltinFunctionNode AddBranch(string name, string path)
    {
        var n = new BuiltinFunctionNode { Name = name, FunctionName = name };
        n.InputPins.Add(MakePin("Exec", PinDirection.Input));
        return Add(n, path);
    }

    private BuiltinFunctionNode AddLoop(string name, string path)
    {
        var n = new BuiltinFunctionNode { Name = name, FunctionName = name };
        n.InputPins.Add(MakePin("Exec", PinDirection.Input));
        n.InputPins.Add(MakePin("List", PinDirection.Input));
        return Add(n, path);
    }

    private BuiltinFunctionNode AddCtrlNode(string name, string path)
    {
        var n = new BuiltinFunctionNode { Name = name, FunctionName = name };
        return Add(n, path);
    }

    // ── Helpers ──

    private T Add<T>(T node, string path) where T : BlueprintNode { node.Id = StableId(path); _bp.Nodes.Add(node); return node; }

    private static string StableId(string path) => $"v6-{path}";

    private static BlueprintPin MakePin(string name, PinDirection dir)
        => new() { Id = Guid.NewGuid().ToString(), Name = name, Direction = dir, Type = PinType.Execution };

    private void ConnectExec(BlueprintNode f, BlueprintNode t)
    {
        var fp = f.OutputPins.Find(p => p.Name == "Exec");
        var tp = t.InputPins.Find(p => p.Name == "Exec");
        if (fp is not null && tp is not null) Connect(f, fp, t, tp);
    }

    private void ConnectNamedExec(BlueprintNode f, string pin, BlueprintNode t)
    {
        var fp = f.OutputPins.Find(p => p.Name == pin);
        var tp = t.InputPins.Find(p => p.Name == "Exec");
        if (fp is not null && tp is not null) Connect(f, fp, t, tp);
    }

    private void ConnectValue(BlueprintNode f, BlueprintNode t)
    {
        var fp = f.OutputPins.Find(p => p.Name == "Value");
        var tp = t.InputPins.Find(p => p.Name == "Value");
        if (fp is not null && tp is not null) Connect(f, fp, t, tp);
    }

    private void Connect(BlueprintNode fn, BlueprintPin fp, BlueprintNode tn, BlueprintPin tp)
        => _bp.Connections.Add(new BlueprintConnection { SourceNodeId = fn.Id, SourcePinId = fp.Id, TargetNodeId = tn.Id, TargetPinId = tp.Id });
}