using Superpower;
using KitX.Workflow.Abstractions;
using System;
using System.Linq;
using Superpower.Model;
using KitX.Workflow.BlockScripting;
using KitX.Workflow.Models;

namespace KitX.Workflow.Test;

static class TestParsing
{
    public static void RunAll(Func<string, bool> shouldRun, IBlockScriptParser parser,
        Action<string, string, bool, string> check)
    {
        Console.WriteLine("\n── P0: Basic parsing ──");

        if (shouldRun("T01")) { var e = BSParser.ParseExpression("42"); check("T01", "integer literal 42", e is BSLiteral { Kind: BSLiteralKind.Integer } lit && (int)lit.Value! == 42, ""); }
        if (shouldRun("T02")) { bool ok = true; void Lit(string s, BSLiteralKind k) { ok &= BSParser.ParseExpression(s) is BSLiteral l && l.Kind == k; } Lit("\"hello\"", BSLiteralKind.String); Lit("3.14", BSLiteralKind.Double); Lit("true", BSLiteralKind.Boolean); Lit("'a'", BSLiteralKind.Char); Lit("null", BSLiteralKind.Null); check("T02", "all literal types", ok, ""); }
        if (shouldRun("T03")) { var e = BSParser.ParseExpression("currentLoop"); check("T03", "identifier reference", e is BSIdentifier id && id.Name == "currentLoop", ""); }
        if (shouldRun("T04")) { var e = BSParser.ParseExpression("Print(\"hi\")"); check("T04", "bare function call", e is BSCall c && c.MethodName == "Print" && c.Args.Count == 1, ""); }
        if (shouldRun("T05")) { var e = BSParser.ParseExpression("TestPlugin.WPF.Core.HelloKitX()"); check("T05", "dotted function call", e is BSCall c && c.MethodName == "HelloKitX" && c.FullMethodName == "TestPlugin.WPF.Core.HelloKitX" && c.Args.Count == 0, ""); }
        if (shouldRun("T06")) { var tokens = BSParser.Tokenize("_"); check("T06", "placeholder _", tokens.ElementAt(0).Kind == BSToken.Placeholder, ""); }
        if (shouldRun("T07")) { var tokens = BSParser.Tokenize("a > StringConcat(\"User: \", _) > Print;"); var stmts = BSParser.StatementList.Parse(tokens); check("T07", "pipeline linear chain", stmts.Count == 1 && stmts[0] is BSPipeline p && p.Targets.Count == 2, ""); }
        if (shouldRun("T08")) { var tokens = BSParser.Tokenize("a, b > StringConcat > Print;"); var stmts = BSParser.StatementList.Parse(tokens); check("T08", "pipeline diamond", stmts.Count == 1 && stmts[0] is BSPipeline p && p.Sources.Count == 2 && p.Targets.Count == 2, ""); }
        if (shouldRun("T09")) { var tokens = BSParser.Tokenize("data > result;"); var stmts = BSParser.StatementList.Parse(tokens); check("T09", "pipeline assignment", stmts.Count == 1 && stmts[0] is BSPipeline p && p.Sources.Count == 1 && p.Targets.Count == 1, ""); }
        if (shouldRun("T10")) { var e = BSParser.ParseExpression("a + \", \" + b"); check("T10", "binary + chain", e is BSBinary b && b.Operator == "+", ""); }
    }
}
