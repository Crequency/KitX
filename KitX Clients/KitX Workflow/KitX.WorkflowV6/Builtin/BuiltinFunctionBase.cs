namespace KitX.WorkflowV6.Builtin;

using KitX.Core.Contract.Workflow;

// ─────────────────────────────────────────────────────────────────────────────
// BuiltinFunctionBase — declarative base for builtin function descriptors.
//
// Every v6 builtin is a descriptor: identity (Name + Kind) plus a port layout
// (InputPorts / OutputPorts, optionally InputVariadic). Before this base existed
// each concrete function copy-pasted the four descriptor members; now a concrete
// builtin is just a class declaration whose parameterless constructor chains the
// literal identity + port specs up to this base. The parameterless constructor is
// mandatory — BuiltinFunctionRegistry.Discover instantiates every builtin via
// Activator.CreateInstance(type) and requires GetConstructor(Type.EmptyTypes).
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Declarative base for builtin function descriptors. Concrete builtins supply their
/// identity (name + behavioural kind) and port layout through a parameterless
/// constructor that chains to this base; the four descriptor members
/// (Name / Kind / InputPorts / OutputPorts) are implemented here once instead of
/// being copy-pasted per function.
/// </summary>
public abstract class BuiltinFunctionBase : IBuiltinFunction
{
    private readonly string _name;
    private readonly FunctionKind _kind;
    private readonly IReadOnlyList<PortSpec> _inputPorts;
    private readonly IReadOnlyList<PortSpec> _outputPorts;
    private readonly VariadicPinSpec? _inputVariadic;

    /// <summary>
    /// Initialises a builtin descriptor with its identity and port layout.
    /// </summary>
    /// <param name="name">The registration key — the function name as it appears in KS source (e.g. "Print", "Range").</param>
    /// <param name="kind">Behavioural kind (Pure / SideEffect). See <see cref="FunctionKind"/>.</param>
    /// <param name="inputPorts">Input port specs (data the function consumes), in source order.</param>
    /// <param name="outputPorts">Output port specs (data the function produces). Empty for SideEffect.</param>
    /// <param name="inputVariadic">Optional variadic input spec (e.g. StringConcat's N extra string pins).</param>
    protected BuiltinFunctionBase(string name, FunctionKind kind,
        IReadOnlyList<PortSpec> inputPorts, IReadOnlyList<PortSpec> outputPorts,
        VariadicPinSpec? inputVariadic = null)
    {
        _name = name;
        _kind = kind;
        _inputPorts = inputPorts;
        _outputPorts = outputPorts;
        _inputVariadic = inputVariadic;
    }

    /// <summary>The registration key — the function name as it appears in KS source.</summary>
    public string Name => _name;

    /// <summary>Behavioural kind (Pure / SideEffect). See <see cref="FunctionKind"/>.</summary>
    public FunctionKind Kind => _kind;

    /// <summary>Input port specs (data the function consumes), in source order.</summary>
    public IReadOnlyList<PortSpec> InputPorts => _inputPorts;

    /// <summary>Output port specs (data the function produces). Empty for SideEffect.</summary>
    public IReadOnlyList<PortSpec> OutputPorts => _outputPorts;

    /// <summary>Optional variadic input spec (e.g. StringConcat's N extra string pins).</summary>
    public VariadicPinSpec? InputVariadic => _inputVariadic;
}
