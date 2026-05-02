using System;
using System.Collections.Generic;
using System.Linq;
using KitX.Core.Contract.Workflow;
using KitX.Core.Contract.Plugin;
using KitX.Core.Device;
using KitX.Core.DI;
using KitX.Shared.CSharp.Plugin;
using Microsoft.CodeAnalysis.CSharp;
using Serilog;

namespace KitX.Core.Workflow;

/// <summary>
/// Plugin coordination service for workflow script processing.
/// Implements IWorkflowPluginService.
/// </summary>
internal class WorkflowPluginService : IWorkflowPluginService
{
    private readonly WorkflowRuntimeState _state;

    /// <summary>
    /// Initializes a new instance of WorkflowPluginService.
    /// </summary>
    /// <param name="state">Shared runtime state.</param>
    internal WorkflowPluginService(WorkflowRuntimeState state)
    {
        _state = state;
    }

    /// <inheritdoc />
    public void InitializePluginManager()
    {
        if (_state.IsParserInitialized) return;

        try
        {
            var pluginServer = ServiceHost.IsInitialized
                ? ServiceHost.GetRequiredService<IPluginServer>()
                : new KitX.Core.Device.PluginsServer(new KitX.Core.Event.EventService());
            var realPluginManager = new RealPluginManager(pluginServer);

            Kscript.CSharp.Parser.Parser.SetPluginManager(realPluginManager);

            _state.IsParserInitialized = true;
            Log.Information("[WorkflowPluginService] Real plugin manager initialized");
        }
        catch (Exception ex)
        {
            Log.Error($"[WorkflowPluginService] Failed to initialize real plugin manager: {ex.Message}, falling back to mock");
            Kscript.CSharp.Parser.Parser.SetPluginManager(new Kscript.CSharp.Parser.Core.MockPluginManager());
            _state.IsParserInitialized = true;
        }
    }

    /// <inheritdoc />
    public void UpdateAvailablePlugins(List<PluginInfo> plugins)
    {
        _state.AvailablePlugins = plugins ?? new List<PluginInfo>();
        Log.Information($"[WorkflowPluginService] Updated available plugins: {_state.AvailablePlugins.Count} plugins");
    }

    /// <inheritdoc />
    public List<VariableConstant> ParseConstantsFromCode(string code)
    {
        var result = new List<VariableConstant>();

        if (string.IsNullOrWhiteSpace(code))
            return result;

        var lines = code.Split('\n');
        var newConstants = new Dictionary<string, VariableConstant>();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("const "))
            {
                var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 4 && parts[0] == "const")
                {
                    var type = parts[1];
                    var name = parts[2];
                    var valueStr = string.Join(" ", parts.Skip(3)).TrimStart('=').Trim().TrimEnd(';');

                    if (!newConstants.ContainsKey(name))
                    {
                        var defaultValue = ParseValue(valueStr, type);

                        newConstants[name] = new VariableConstant
                        {
                            Name = name,
                            DefaultValue = defaultValue,
                            UserValue = defaultValue,
                            Type = type
                        };
                    }
                }
            }
        }

        return newConstants.Values.ToList();
    }

    /// <summary>
    /// Parses a constant value string into the appropriate typed value.
    /// </summary>
    private static object? ParseValue(string valueStr, string type)
    {
        if (string.IsNullOrEmpty(valueStr)) return null;

        try
        {
            return type switch
            {
                "int" => int.TryParse(valueStr, out var i) ? i : 0,
                "double" => double.TryParse(valueStr, out var d) ? d : 0.0,
                "float" => float.TryParse(valueStr, out var f) ? f : 0.0f,
                "bool" => bool.TryParse(valueStr, out var b) && b,
                "string" => valueStr.Trim('"').Trim('\''),
                _ => valueStr
            };
        }
        catch
        {
            return valueStr;
        }
    }

    /// <inheritdoc />
    public string ApplyConstantsToCode(string code, List<VariableConstant> constants)
    {
        if (string.IsNullOrWhiteSpace(code) || constants == null || !constants.Any())
            return code;

        var constantValues = new Dictionary<string, object?>();
        foreach (var constant in constants)
        {
            var typedValue = constant.UserValue;
            if (typedValue != null && constant.Type != null)
            {
                typedValue = ConvertToTypedValue(typedValue, constant.Type);
            }
            constantValues[constant.Name] = typedValue;
        }

        var tree = CSharpSyntaxTree.ParseText(code);
        var root = tree.GetRoot();

        var rewriter = new ConstantValueRewriter(constantValues);
        var newRoot = rewriter.Visit(root);

        return newRoot.ToFullString();
    }

    /// <summary>
    /// Converts a value to the specified type.
    /// </summary>
    private static object? ConvertToTypedValue(object? value, string type)
    {
        if (value == null) return null;

        return type.ToLowerInvariant() switch
        {
            "int" => Convert.ToInt32(value),
            "long" => Convert.ToInt64(value),
            "double" => Convert.ToDouble(value),
            "float" => Convert.ToSingle(value),
            "decimal" => Convert.ToDecimal(value),
            "bool" or "boolean" => Convert.ToBoolean(value),
            "string" => value.ToString(),
            "char" => Convert.ToChar(value),
            _ => value
        };
    }

    /// <inheritdoc />
    public string MergeHelperFunctions(string mainCode, List<HelperFunction> helperFunctions)
    {
        return BlockScripting.HelperFunctionCodeGenerator.MergeWithMainProgram(mainCode, helperFunctions);
    }
}
