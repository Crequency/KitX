using Microsoft.Extensions.DependencyInjection;
using KitX.Core.DI;
using KitX.Core.Contract.Workflow;
using KitX.Workflow.Abstractions;
using KitX.Workflow.BlockScripting;
using KitX.Workflow.CFG;
using KitX.Workflow.Conversion;

namespace KitX.Workflow.Test.Xunit;

/// <summary>
/// Shared DI fixture for the xUnit test suite. Builds the same service graph as the
/// existing console harness (<c>KitX.Workflow.Test/Program.cs</c>): resolves
/// <see cref="IBlockScriptParser"/> and <see cref="BuiltinFunctionRegistry"/> once per
/// test class via <see cref="CoreServiceCollectionExtensions.AddCoreServices"/>.
/// </summary>
public sealed class WorkflowFixture
{
    private readonly ServiceProvider _provider;

    public WorkflowFixture()
    {
        var services = new ServiceCollection();
        services.AddCoreServices();
        _provider = services.BuildServiceProvider();
        Parser = _provider.GetRequiredService<IBlockScriptParser>();
        FunctionRegistry = _provider.GetRequiredService<BuiltinFunctionRegistry>();
    }

    public IBlockScriptParser Parser { get; }
    public BuiltinFunctionRegistry FunctionRegistry { get; }

    /// <summary>
    /// Resolves a service from the DI container. Throws if unregistered — RED-LIGHT tests for
    /// unimplemented services (ICFGGraphRenderer, ICFGDiffer) rely on this throwing until the
    /// implementation is registered.
    /// </summary>
    public T GetService<T>() where T : notnull => _provider.GetRequiredService<T>();

    /// <summary>
    /// BS → CFG → BS text round-trip helper (mirrors <c>Program.CfgRoundTrip</c>).
    /// Returns the rendered BS text, or null if parsing failed.
    /// </summary>
    public string? CfgRoundTrip(string source, List<HelperFunction>? helpers = null)
    {
        var pr = Parser.Parse(source);
        if (!pr.IsSuccess || pr.Script == null) return null;
        pr.Script.HelperFunctions = helpers ?? [];

        var context = new ForwardConversionState { Script = pr.Script };
        if (pr.Script.ConstBlock != null)
            foreach (var v in pr.Script.ConstBlock.Variables)
                if (!context.PubVarNames.Contains(v.Name)) context.PubVarNames.Add(v.Name);
        if (pr.Script.PubVarBlock != null)
            foreach (var v in pr.Script.PubVarBlock.Variables)
                if (!context.PubVarNames.Contains(v.Name)) context.PubVarNames.Add(v.Name);

        var cfg = ConversionPaths.BS2CFG(pr.Script, helpers ?? [], FunctionRegistry, context);

        if (pr.Script.ConstBlock != null)
            foreach (var v in pr.Script.ConstBlock.Variables)
                cfg.ConstDeclarations.Add(new ConstDeclaration { Name = v.Name, Type = v.Type, DefaultValue = v.DefaultValue });
        if (pr.Script.PubVarBlock != null)
            foreach (var v in pr.Script.PubVarBlock.Variables)
            {
                if (!cfg.PubVarDeclarations.Contains(v.Name)) cfg.PubVarDeclarations.Add(v.Name);
                if (!string.IsNullOrEmpty(v.Type) && !cfg.PubVarTypes.ContainsKey(v.Name)) cfg.PubVarTypes[v.Name] = v.Type;
            }

        return new CFGRenderer().Render(cfg);
    }

    /// <summary>
    /// BS → CFG helper returning the built <see cref="ControlFlowGraph"/>, or null on parse failure.
    /// </summary>
    public ControlFlowGraph? BS2CFG(string source, List<HelperFunction>? helpers = null)
    {
        var pr = Parser.Parse(source);
        if (!pr.IsSuccess || pr.Script == null) return null;
        pr.Script.HelperFunctions = helpers ?? [];

        var context = new ForwardConversionState { Script = pr.Script };
        if (pr.Script.ConstBlock != null)
            foreach (var v in pr.Script.ConstBlock.Variables)
                if (!context.PubVarNames.Contains(v.Name)) context.PubVarNames.Add(v.Name);
        if (pr.Script.PubVarBlock != null)
            foreach (var v in pr.Script.PubVarBlock.Variables)
                if (!context.PubVarNames.Contains(v.Name)) context.PubVarNames.Add(v.Name);

        return ConversionPaths.BS2CFG(pr.Script, helpers ?? [], FunctionRegistry, context);
    }
}
