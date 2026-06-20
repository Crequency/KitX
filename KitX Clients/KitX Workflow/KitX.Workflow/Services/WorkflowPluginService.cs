using KitX.Core.Contract.Workflow;
using KitX.Core.Contract.Plugin;
using KitX.Shared.CSharp.Plugin;
using Serilog;

namespace KitX.Workflow.Services;

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
}
