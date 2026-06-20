using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using KitX.Core.DI;
using KitX.Core.Contract.Workflow;
using KitX.Workflow.BlockScripting;
using KitX.Workflow.Compilation;
using KitX.Workflow.Abstractions;
using KitX.Workflow.Abstractions.Models;
using KitX.Workflow.Abstractions.Models.Statements;
using KitX.Workflow.Abstractions.Models.Results;
using KitX.Workflow.Blueprint;
using KitX.Workflow.CFG;
using KitX.Workflow.Conversion;

namespace KitX.Workflow.Test;

/// <summary>
/// Compiles a .kcs workflow file end-to-end (parse + BS→CFG→CS→assembly) and reports
/// diagnostics. Invoked via <c>--kcs &lt;path&gt;</c>. With the additional
/// <c>--roundtrip</c> flag, also runs an optional BS→BP→BS→Compile round-trip to
/// surface converter regressions that a one-way compile would hide. Useful for
/// validating real workflow scripts without running the Dashboard.
/// </summary>
public partial class Program
{
    public static void RunKcsCompileTest(string kcsPath, bool withRoundTrip = false)
    {
        Console.WriteLine($"=== KCS Compile Test: {kcsPath} ===" +
            (withRoundTrip ? " (with round-trip)" : ""));

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

        bool compileOk = result != null && errors.Count == 0;

        // Optional round-trip phase — surfaces converter bugs that a single compile hides.
        // Mirrors the BS→BP→BS→Compile pattern from Test V but driven by a real .kcs file.
        bool roundTripOk = true;
        if (withRoundTrip)
        {
            Console.WriteLine();
            Console.WriteLine("--- Round-trip phase (BS → BP → BS → Compile) ---");
            try
            {
                var nodeRegistry = sp.GetRequiredService<INodeRegistry>();
                var layoutService = sp.GetRequiredService<ILayoutService>();
                var reverseConverter = sp.GetRequiredService<IBlueprintToBlockScriptConverter>();
                var funcRegistry = sp.GetRequiredService<BuiltinFunctionRegistry>();
                var converter = new BlockScriptToBlueprintConverter(parser, nodeRegistry, layoutService, funcRegistry);

                var helpers = kcs.HelperFunctions ?? new System.Collections.Generic.List<HelperFunction>();

                // BS → BP
                var bp = converter.Convert(sourceCode, helpers);
                if (bp == null)
                {
                    Console.WriteLine("  FAIL: BS→BP conversion returned null");
                    if (converter.LastDiagnostics != null)
                        Console.WriteLine($"       Diagnostics: {converter.LastDiagnostics.Format()}");
                    roundTripOk = false;
                }
                else
                {
                    Console.WriteLine("  BS→BP: OK");

                    // BP → BS
                    var bsResult = reverseConverter.Convert(bp);
                    if (string.IsNullOrEmpty(bsResult))
                    {
                        Console.WriteLine("  FAIL: BP→BS conversion returned empty");
                        roundTripOk = false;
                    }
                    else
                    {
                        Console.WriteLine("  BP→BS: OK");
                        Console.WriteLine("  --- Round-tripped BlockScript ---");
                        Console.WriteLine(bsResult);
                        Console.WriteLine("  --- end ---");

                        // Parse + compile the round-tripped source
                        var pr2 = parser.Parse(bsResult);
                        if (!pr2.IsSuccess || pr2.Script == null)
                        {
                            Console.WriteLine($"  FAIL: round-tripped BS parse error: {pr2.ErrorMessage}");
                            roundTripOk = false;
                        }
                        else
                        {
                            pr2.Script.HelperFunctions = helpers;
                            var compiled2 = new CSCompiler().CompileScript(pr2.Script, workflowId: kcs.Id, out var errors2);
                            if (compiled2 == null)
                            {
                                Console.WriteLine($"  FAIL: round-tripped BS compile null, {errors2.Count} error(s)");
                                foreach (var e in errors2.Take(10)) Console.WriteLine($"         {e}");
                                roundTripOk = false;
                            }
                            else
                            {
                                Console.WriteLine("  Round-tripped BS compile: OK");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  FAIL: round-trip exception: {ex.Message}");
                roundTripOk = false;
            }
        }

        // Final verdict combines the one-way compile and (if requested) the round-trip.
        bool overall = compileOk && roundTripOk;
        Console.WriteLine();
        if (withRoundTrip)
            Console.WriteLine($"RESULT: {(overall ? "PASS" : "FAIL")} (compile={compileOk}, roundtrip={roundTripOk})");
        else if (compileOk)
            Console.WriteLine("\nRESULT: PASS");
        else
            Console.WriteLine("\nRESULT: FAIL");
    }
}