using KitX.Workflow.Abstractions;
using System;
using Microsoft.Extensions.DependencyInjection;
using KitX.Core.Contract.Workflow;
using KitX.Workflow.BlockScripting;
using KitX.Workflow.Compilation;
using KitX.Workflow.Conversion;

namespace KitX.Workflow.Test;

static class TestInfra
{
    public static void RunAll(
        Func<string, bool> shouldRun,
        IBlockScriptParser parser,
        BlockScriptToBlueprintConverter converter,
        IBlueprintToBlockScriptConverter reverseConverter,
        IServiceProvider sp,
        Action<string, string> pass,
        Action<string, string, string> fail,
        Action<string, string, bool, string> check)
    {
        // Placeholder for future infrastructure-level tests.
        // Currently no T00-level tests — all are in domain-specific groups.
    }
}
