using System;
using System.Collections.Generic;
using System.Linq;
using KitX.Core.Contract.Workflow;
using KitX.Workflow.Abstractions;
using KitX.Workflow.BlockScripting;
using KitX.Workflow.Conversion;

namespace KitX.Workflow.Test;

static class TestRoundTrip
{
    public static void RunAll(Func<string, bool> shouldRun, IBlockScriptParser parser,
        BlockScriptToBlueprintConverter converter, IBlueprintToBlockScriptConverter reverseConverter,
        Func<List<HelperFunction>> getDeclHelpers,
        Func<IBlockScriptParser, BlockScriptToBlueprintConverter, IBlueprintToBlockScriptConverter, string, List<HelperFunction>, string> roundTrip,
        Func<string, string, bool> textEquals,
        Action<string, string> pass, Action<string, string, string> fail,
        Action<string, string, bool, string> check)
    {
        Console.WriteLine("\n-- P4: Round-trip consistency --");
        var h = getDeclHelpers();
        var End = "\n\n#Block End\nPrint(\"done\");\nBreak();";
        var guessingGame = @"#ConstBlock
int guessNum = 5;
int targetNum = 7;
int loopMax = 3;

#PubVarBlock
bool cond;

#MainBlock
Print(""开始执行工作流"");
Goto(""ForLoopBlock"");

#Block ForLoopBlock
ForLoop(0, loopMax, 1, ""i"", ""LoopBody"", ""EndLogic"");

#Block LoopBody
i > Print;
guessNum, targetNum > HelperFuncCompare(""BEQ"") > cond;
Branch(cond, ""SuccessLogic"", ""CheckLogic"");

#Block CheckLogic
guessNum, targetNum > HelperFuncCompare(""BLT"") > cond;
Branch(cond, ""LessThanLogic"", ""GreaterThanLogic"");

#Block LessThanLogic
Print(""猜小了"");
Goto(""ForLoopBlock"");

#Block GreaterThanLogic
Print(""猜大了"");
Goto(""ForLoopBlock"");

#Block SuccessLogic
Print(""猜对啦！"");
Goto(""EndLogic"");

#Block EndLogic
Print(""示例工作流结束"");
Break();";

        var whileDo = @"#ConstBlock
int guessNum = 5;
int targetNum = 7;

#PubVarBlock
int currentLoop;
bool cond;

#MainBlock
0 > currentLoop;
Goto(""LoopCond"");

#Block LoopCond
currentLoop > HelperFuncCompare(""BLE"", _, 100) > cond;
Branch(cond, ""LoopBody"", ""EndLogic"");

#Block LoopBody
currentLoop > Print;
currentLoop > HelperFuncAdd(_, 1) > currentLoop;
Goto(""LoopCond"");

#Block EndLogic
Print(""条件循环结束"");
Break();";

        if (shouldRun("T39")) { var src = "#ConstBlock\nstring name = \"World\";\n\n#MainBlock\nname > StringConcat(\"Hi \", _) > Print;\nGoto(\"End\");" + End; var rt = roundTrip(parser, converter, reverseConverter, src, h); if (rt != null && textEquals(src, rt)) pass("T39", "basic round-trip"); else fail("T39", "basic round-trip", "not text-stable yet"); }
        if (shouldRun("T40")) { var src = "#PubVarBlock\nint currentLoop;\nstring userInput;\n\n#MainBlock\n0 > currentLoop;\nGoto(\"End\");" + End; var rt = roundTrip(parser, converter, reverseConverter, src, h); bool ok = rt != null && rt.Contains("int currentLoop") && rt.Contains("string userInput"); if (ok) pass("T40", "strong type PubVar"); else fail("T40", "strong type PubVar", "lost types"); }
        if (shouldRun("T41")) { var rt = roundTrip(parser, converter, reverseConverter, guessingGame, h); bool ok = rt != null && !rt.Contains("vaaa"); if (ok) pass("T41", "no capacitor leak"); else fail("T41", "no capacitor leak", "vaaa leaked"); }
        if (shouldRun("T42")) { var rt = roundTrip(parser, converter, reverseConverter, guessingGame, h); check("T42", "ConstBlock preserved", rt != null && rt.Contains("int guessNum = 5;") && rt.Contains("int targetNum = 7;"), ""); }
        if (shouldRun("T43")) { var src = "#ConstBlock\nstring name = \"World\";\n\n#MainBlock\nname > StringConcat(\"Hi \", _) > Print;\nGoto(\"End\");" + End; var rt = roundTrip(parser, converter, reverseConverter, src, h); bool ok = rt != null && rt.Contains(">") && !rt.Contains(" = "); if (ok) pass("T43", "pipeline form preserved"); else fail("T43", "pipeline form preserved", "contains ="); }
        if (shouldRun("T44")) { var rt = roundTrip(parser, converter, reverseConverter, guessingGame, h); check("T44", "control flow form preserved", rt != null && rt.Contains("Branch(") && !rt.Contains("NextBlock = Branch("), ""); }
        if (shouldRun("T45")) { var rt = roundTrip(parser, converter, reverseConverter, guessingGame, h); check("T45", "Goto form preserved", rt != null && rt.Contains("Goto(") && !rt.Contains("NextBlock = \""), ""); }
        if (shouldRun("T46")) { var rt = roundTrip(parser, converter, reverseConverter, guessingGame, h); if (rt != null && textEquals(guessingGame, rt)) pass("T46", "guessing game round-trip"); else fail("T46", "guessing game round-trip", "not text-stable yet"); }
        if (shouldRun("T47")) { var rt = roundTrip(parser, converter, reverseConverter, whileDo, h); if (rt != null && textEquals(whileDo, rt)) pass("T47", "while-do round-trip"); else fail("T47", "while-do round-trip", "not text-stable yet"); }
        if (shouldRun("T48")) { var bvSrc = "#Block ProcessBatch\n##BlockVars\nint processedCount = 0;\nstring currentItem;\n##BlockBody\n\"item1\" > currentItem;\ncurrentItem > Print;\nprocessedCount > HelperFuncAdd(_, 1) > processedCount;\nprocessedCount > Print;\nGoto(\"NextStage\");\n\n#MainBlock\nGoto(\"ProcessBatch\");\n\n#Block NextStage\nPrint(\"done\");\nBreak();"; var rt = roundTrip(parser, converter, reverseConverter, bvSrc, h); if (rt != null && textEquals(bvSrc, rt)) pass("T48", "BlockVar round-trip"); else fail("T48", "BlockVar round-trip", "not text-stable yet"); }
    }
}
