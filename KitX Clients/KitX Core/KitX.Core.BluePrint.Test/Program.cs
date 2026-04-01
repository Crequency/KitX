using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using KitX.Core.DI;
using KitX.Core.Contract.Workflow;
using KitX.Core.Workflow.Blueprint;

namespace KitX.Core.BluePrint.Test;

public class Program
{
    public static void Main(string[] args)
    {
        Console.WriteLine("╔════════════════════════════════════════════════════════╗");
        Console.WriteLine("║       KitX Core - BlurPrint (Blueprint) Test          ║");
        Console.WriteLine("╚════════════════════════════════════════════════════════╝\n");

        // ============================================================
        // Step 1: Initialize DI Container
        // ============================================================
        var services = new ServiceCollection();
        services.AddCoreServices();
        var serviceProvider = services.BuildServiceProvider();

        // Get IBlockScriptParser from DI
        var parser = serviceProvider.GetRequiredService<IBlockScriptParser>();
        Console.WriteLine("✓ DI Container initialized");
        Console.WriteLine($"✓ IBlockScriptParser resolved: {parser.GetType().Name}\n");

        // ============================================================
        // Step 2: Create Services for Conversion
        // ============================================================
        var connectionCreationService = new ConnectionCreationService();
        var flowProcessingService = new FlowProcessingService(connectionCreationService);
        var layoutService = new LayoutService();

        // Note: IBlockScriptToBlueprintConverter is NOT in DI, manually instantiate
        var converter = new BlockScriptToBlueprintConverter(
            parser,
            flowProcessingService,
            connectionCreationService,
            layoutService);

        // ============================================================
        // Step 3: Sample BlockScript Source Code
        // ============================================================
        var sourceCode = GetSampleBlockScript();

        // ============================================================
        // Step 4: Parse BlockScript
        // ============================================================
        Console.WriteLine("┌─────────────────────────────────────────────────────────┐");
        Console.WriteLine("│ Parsing BlockScript                                     │");
        Console.WriteLine("└─────────────────────────────────────────────────────────┘\n");

        var parseResult = parser.Parse(sourceCode);

        if (!parseResult.IsSuccess || parseResult.Script == null)
        {
            Console.WriteLine($"✗ Parse failed: {parseResult.ErrorMessage}");
            Console.WriteLine($"  Error at line: {parseResult.ErrorLine}");
            return;
        }

        Console.WriteLine("✓ BlockScript parsed successfully\n");

        var script = parseResult.Script;

        // Display parsed structure
        Console.WriteLine("  Parsed Structure:");
        Console.WriteLine($"    - ConstBlock: {(script.ConstBlock != null ? script.ConstBlock.Name : "null")}");
        Console.WriteLine($"    - PubVarBlock: {(script.PubVarBlock != null ? script.PubVarBlock.Name : "null")}");
        Console.WriteLine($"    - MainBlock: {(script.MainBlock != null ? script.MainBlock.Name : "null")}");
        Console.WriteLine($"    - NamedBlocks: {script.NamedBlocks.Count}");
        Console.WriteLine($"    - LoopBlocks: {script.LoopBlocks.Count}");
        Console.WriteLine($"    - HelperFunctions: {script.HelperFunctions.Count}\n");

        // ============================================================
        // Step 5: Convert to Blueprint
        // ============================================================
        Console.WriteLine("┌─────────────────────────────────────────────────────────┐");
        Console.WriteLine("│ Converting to Blueprint                                  │");
        Console.WriteLine("└─────────────────────────────────────────────────────────┘\n");

        Contract.Workflow.Blueprint blueprint;
        try
        {
            blueprint = converter.Convert(script);
            Console.WriteLine("✓ Blueprint created successfully\n");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Conversion failed: {ex.Message}");
            Console.WriteLine($"  Stack trace: {ex.StackTrace}");
            return;
        }

        // ============================================================
        // Step 6: Display Blueprint Summary
        // ============================================================
        Console.WriteLine("┌─────────────────────────────────────────────────────────┐");
        Console.WriteLine("│ Blueprint Summary                                        │");
        Console.WriteLine("└─────────────────────────────────────────────────────────┘\n");

        Console.WriteLine($"  Name: {blueprint.Name}");
        Console.WriteLine($"  ID: {blueprint.Id}");
        Console.WriteLine($"  Nodes: {blueprint.Nodes.Count}");
        Console.WriteLine($"  Connections: {blueprint.Connections.Count}");
        Console.WriteLine($"  HelperFunctions: {blueprint.HelperFunctions.Count}");
        Console.WriteLine($"  PubVarNames: {blueprint.PubVarNames.Count}");
        Console.WriteLine($"  ConstValues: {blueprint.ConstValues.Count}\n");

        // ============================================================
        // Step 7: Display HelperFunctions
        // ============================================================
        if (blueprint.HelperFunctions.Count > 0)
        {
            Console.WriteLine("┌─────────────────────────────────────────────────────────┐");
            Console.WriteLine("│ HelperFunctions                                          │");
            Console.WriteLine("└─────────────────────────────────────────────────────────┘\n");

            foreach (var helper in blueprint.HelperFunctions)
            {
                Console.WriteLine($"  • {helper.Name}");
                if (helper.Parameters.Count > 0)
                {
                    var @params = string.Join(", ", helper.Parameters.Select(p => $"{p.Type} {p.Name}"));
                    Console.WriteLine($"      Parameters: {@params}");
                }
            }
            Console.WriteLine();
        }

        // ============================================================
        // Step 8: Display PubVarNames
        // ============================================================
        if (blueprint.PubVarNames.Count > 0)
        {
            Console.WriteLine("┌─────────────────────────────────────────────────────────┐");
            Console.WriteLine("│ PubVarNames (Public Variables)                           │");
            Console.WriteLine("└─────────────────────────────────────────────────────────┘\n");

            foreach (var pubVar in blueprint.PubVarNames)
            {
                Console.WriteLine($"  • {pubVar}");
            }
            Console.WriteLine();
        }

        // ============================================================
        // Step 9: Display ConstValues
        // ============================================================
        if (blueprint.ConstValues.Count > 0)
        {
            Console.WriteLine("┌─────────────────────────────────────────────────────────┐");
            Console.WriteLine("│ ConstValues (Variable Constants)                         │");
            Console.WriteLine("└─────────────────────────────────────────────────────────┘\n");

            foreach (var vc in blueprint.ConstValues)
            {
                Console.WriteLine($"  • {vc.Name}: {vc.Type} = {vc.DefaultValue}");
            }
            Console.WriteLine();
        }

        // ============================================================
        // Step 10: Display Nodes by Type
        // ============================================================
        Console.WriteLine("┌─────────────────────────────────────────────────────────┐");
        Console.WriteLine("│ Nodes (Grouped by Type)                                  │");
        Console.WriteLine("└─────────────────────────────────────────────────────────┘\n");

        var nodesByType = blueprint.Nodes
            .GroupBy(n => n.NodeType)
            .OrderBy(g => g.Key.ToString());

        foreach (var group in nodesByType)
        {
            Console.WriteLine($"  [{group.Key}] ({group.Count()} nodes)");
            foreach (var node in group)
            {
                var inputPins = string.Join(", ",
                    node.InputPins.Select(p => $"{p.Name}({p.Direction}, {p.Type})"));
                var outputPins = string.Join(", ",
                    node.OutputPins.Select(p => $"{p.Name}({p.Direction}, {p.Type})"));

                // Type-specific extra info
                var extra = node switch
                {
                    ConstNode cn => $" | ConstName={cn.ConstName}, Value={cn.ConstValue}",
                    CallNode call => $" | Function={call.FunctionName}",
                    CallHelperNode helper => $" | Helper={helper.HelperFunctionName}",
                    GetNode gn => $" | VarName={gn.VarName}",
                    SetNode sn => $" | VarName={sn.VarName}",
                    BranchNode bn => $" | Type=Branch",
                    LoopNode ln => $" | Type=Loop",
                    EntryNode en => $" | Type=Entry",
                    BreakNode brk => $" | Type=Break",
                    PrintNode pn => $" | Type=Print",
                    PauseNode pause => $" | Type=Pause",
                    _ => ""
                };

                Console.WriteLine($"    • {node.Name} ({node.Id})");

                // 显示输入引脚，包含预设值
                var inputPinsWithDefault = string.Join(", ",
                    node.InputPins.Select(p => p.DefaultValue != null
                        ? $"{p.Name}({p.Direction}, {p.Type}) = \"{p.DefaultValue}\""
                        : $"{p.Name}({p.Direction}, {p.Type})"));
                Console.WriteLine($"        Inputs: [{inputPinsWithDefault}]");

                // 显示输出引脚
                var outputPinsWithDefault = string.Join(", ",
                    node.OutputPins.Select(p => p.DefaultValue != null
                        ? $"{p.Name}({p.Direction}, {p.Type}) = \"{p.DefaultValue}\""
                        : $"{p.Name}({p.Direction}, {p.Type})"));
                Console.WriteLine($"        Outputs: [{outputPinsWithDefault}]{extra}");
            }
            Console.WriteLine();
        }

        // ============================================================
        // Step 11: Display Connections (Exec vs Data)
        // ============================================================
        Console.WriteLine("┌─────────────────────────────────────────────────────────┐");
        Console.WriteLine("│ Connections                                              │");
        Console.WriteLine("└─────────────────────────────────────────────────────────┘\n");

        var execConnections = new List<BlueprintConnection>();
        var dataConnections = new List<BlueprintConnection>();

        foreach (var conn in blueprint.Connections)
        {
            var sourceNode = blueprint.GetNodeById(conn.SourceNodeId);
            var targetNode = blueprint.GetNodeById(conn.TargetNodeId);
            var sourcePin = sourceNode?.OutputPins.FirstOrDefault(p => p.Id == conn.SourcePinId);
            var targetPin = targetNode?.InputPins.FirstOrDefault(p => p.Id == conn.TargetPinId);

            if (sourcePin?.Type == PinType.Execution || targetPin?.Type == PinType.Execution)
                execConnections.Add(conn);
            else
                dataConnections.Add(conn);
        }

        Console.WriteLine($"  Exec Connections: {execConnections.Count}");
        foreach (var conn in execConnections)
        {
            var src = blueprint.GetNodeById(conn.SourceNodeId);
            var tgt = blueprint.GetNodeById(conn.TargetNodeId);
            var sp = src?.OutputPins.FirstOrDefault(p => p.Id == conn.SourcePinId);
            var tp = tgt?.InputPins.FirstOrDefault(p => p.Id == conn.TargetPinId);
            Console.WriteLine($"    • {src?.Name}[{src?.Id.Substring(0,8)}].{sp?.Name} -> {tgt?.Name}[{tgt?.Id.Substring(0,8)}].{tp?.Name}");
        }

        Console.WriteLine($"\n  Data Connections: {dataConnections.Count}");
        foreach (var conn in dataConnections)
        {
            var src = blueprint.GetNodeById(conn.SourceNodeId);
            var tgt = blueprint.GetNodeById(conn.TargetNodeId);
            var sp = src?.OutputPins.FirstOrDefault(p => p.Id == conn.SourcePinId);
            var tp = tgt?.InputPins.FirstOrDefault(p => p.Id == conn.TargetPinId);
            var pubvar = conn.PubVarName != null ? $" (PubVar: {conn.PubVarName})" : "";
            Console.WriteLine($"    • {src?.Name}[{src?.Id.Substring(0,8)}].{sp?.Name} -> {tgt?.Name}[{tgt?.Id.Substring(0,8)}].{tp?.Name}{pubvar}");
        }

        Console.WriteLine("\n═════════════════════════════════════════════════════════");
        Console.WriteLine("  Test Complete");
        Console.WriteLine("═════════════════════════════════════════════════════════\n");
    }

    private static string GetSampleBlockScript()
    {
        string sourceCode = @"#ConstBlock
int guessNum = 5;  // 可变常量，用户在UI中可修改
int loopMax = 3;
int targetNum = 7;
int currentLoop;	// 无预赋值（值初始化），用户无法在UI中修改
// 这个currentLoop为什么不能放在PubVarBlock中：
// 它不是“一次性”的“边数据承载”变量，它是多处、多次使用且随运行而需要变化并持久存储的变量，它的最短生命周期远长于PubVarBlock中的一次性变量（赋值-使用1次后即可销毁，下次用到再重新创建）

#PubVarBlock
// 自动生成，为蓝图预留(是那些数据边为了临时承载数据而使用的变量）
// 除非你知道自己在做什么并且完全了解块脚本与蓝图互译的过程，否则不要在这个块中添加、删除或修改代码
// 直接编写BlockScript时不需要在这里设置变量
bool vaaa0001;
int vaaa0002;

#MainBlock
Print(""开始执行工作流"");
Set(""currentLoop"", 0);
// NextBlock = Loop(HelperFuncCompare(""BLE"", Get(""currentLoop""), loopMax), ""LoopBody"", ""EndLogic""); // 转化前的语句（有嵌套调用）
vaaa0001 = HelperFuncCompare(""BLE"", Get(""currentLoop""), loopMax);  // 脚本转蓝图后，再转回块脚本时，就会利用PubVarBlock中生成的“临时变量”来生成这样的语句（拆分嵌套调用）
NextBlock = Loop(vaaa0001, ""LoopBody"", ""EndLogic"");  // 主循环

#Block LoopBody
// Print(Get(currentLoop)); // 原始嵌套调用用法
// vaaa0001 = Get(currentLoop);
// Print(vaaa0001); // 其实逻辑上等价但是不推荐的方案──反正PubVarBlock的变量池是“无限大”的，没必要重复使用同一个变量。
vaaa0002 = Get(""currentLoop"");
Print(vaaa0002); // 更优的做法，每个临时变量实际上绑定了一条数据边。这样也方便后续直接对蓝图脚本进行Debug时监测数据边上的数据
Set(""currentLoop"", HelperFuncAdd(Get(""currentLoop""), 1));  // 函数嵌套调用，记得在默认的HelperFunction初始化程序中添加这个HelperFuncAdd
NextBlock = Branch(
    HelperFuncCompare(""BEQ"", guessNum, targetNum),  // ✅ 函数调用
    ""SuccessLogic"",
    ""CheckLogic""
);

#Block CheckLogic
NextBlock = Branch(
    HelperFuncCompare(""BLT"", guessNum, targetNum),
    ""LessThanLogic"",
    ""GreaterThanLogic""
);

#Block LessThanLogic
Print(""猜小了"");  // 其实用Print()也行
vaaa0001 = HelperFuncCompare(""BLE"", Get(""currentLoop""), loopMax);  // 原先Loop中的内置嵌套condition表达式由于被拆分，需要在LoopBodyEnd被调用前进行结算，以保持逻辑一致性
NextBlock = LoopBodyEnd(""MainBlock"");  // 返回到 MainBlock 的 Loop

#Block GreaterThanLogic
Print(""猜大了"");
vaaa0001 = HelperFuncCompare(""BLE"", Get(""currentLoop""), loopMax);  // 原先Loop中的内置嵌套condition表达式由于被拆分，需要在LoopBodyEnd被调用前进行结算，以保持逻辑一致性
// 这里仍是vaaa0001是因为在蓝图中实际上是一条边：CallHelper:HelperFuncCompare(BLE).Return --> Loop.Condition | PubVar=vaaa0001
NextBlock = LoopBodyEnd(""MainBlock"");  // 返回到 MainBlock 的 Loop

#Block SuccessLogic
Print(""猜对啦！""); // 这个Block没有Branch/Loop/LoopBodyEnd，自然进入下一行

#Block EndLogic
Print(""示例工作流结束"");";
        return sourceCode;
    }
}
