using System;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using KitX.Core.DI;
using KitX.Core.Contract.Workflow;
using KitX.Workflow.BlockScripting;
using KitX.Workflow.Contract;
using KitX.Workflow.Contract.Models;

namespace KitX.Core.BluePrint.Test;

/// <summary>
/// Compiles a .kcs workflow file end-to-end (parse + BS→CFG→CS→assembly) and reports
/// diagnostics. Invoked via <c>--kcs &lt;path&gt;</c>. Useful for validating real workflow
/// scripts without running the Dashboard.
/// </summary>
public partial class Program
{
    public static void RunKcsCompileTest(string kcsPath)
    {
        Console.WriteLine($"=== KCS Compile Test: {kcsPath} ===");

        if (!File.Exists(kcsPath))
        {
            Console.WriteLine($"FAIL: File not found: {kcsPath}");
            return;
        }

        var services = new ServiceCollection();
        services.AddCoreServices();
        var sp = services.BuildServiceProvider();
        var parser = sp.GetRequiredService<IBlockScriptParser>();

        KcsFileFormat? kcs;
        try
        {
            kcs = JsonSerializer.Deserialize<KcsFileFormat>(File.ReadAllText(kcsPath));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAIL: JSON parse error: {ex.Message}");
            return;
        }
        if (kcs == null || string.IsNullOrEmpty(kcs.BlockScriptSource))
        {
            Console.WriteLine("FAIL: Empty BlockScriptSource");
            return;
        }

        Console.WriteLine($"Name: {kcs.Name}");
        Console.WriteLine($"Id: {kcs.Id}");
        Console.WriteLine($"Trigger: {kcs.TriggerType}");
        Console.WriteLine($"UseBlockMode: {kcs.UseBlockMode}");
        Console.WriteLine($"Helpers: {(kcs.HelperFunctions?.Count ?? 0)}");
        Console.WriteLine();

        var sourceCode = kcs.BlockScriptSource;
        Console.WriteLine("--- BlockScript source ---");
        Console.WriteLine(sourceCode);
        Console.WriteLine("--- end ---\n");

        // Parse
        var pr = parser.Parse(sourceCode);
        Console.WriteLine($"Parse: {(pr.IsSuccess ? "OK" : "FAIL")}, err: {pr.ErrorMessage}");
        if (pr.Script == null)
        {
            Console.WriteLine("FAIL: Script is null after parse");
            return;
        }

        // Attach helper functions from the kcs file
        if (kcs.HelperFunctions != null)
            pr.Script.HelperFunctions = kcs.HelperFunctions;

        // Compile
        var compiler = new CSCompiler();
        var result = compiler.CompileScript(pr.Script, workflowId: kcs.Id, out var errors);
        Console.WriteLine($"Compiled: {(result == null ? "NULL" : "OK")}, errors: {errors.Count}");
        foreach (var e in errors)
            Console.WriteLine("  ERR: " + e);

        if (result != null && errors.Count == 0)
            Console.WriteLine("\nRESULT: PASS");
        else
            Console.WriteLine("\nRESULT: FAIL");
    }
}