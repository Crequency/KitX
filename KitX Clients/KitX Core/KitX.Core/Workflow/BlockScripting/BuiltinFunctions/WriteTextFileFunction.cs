using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using KitX.Core.Contract.Workflow;
using KitX.Core.Workflow.Blueprint;
using KitX.Core.Workflow.Blueprint.CFG;
using KitX.Core.Workflow.Blueprint.Pipeline;
using Serilog;

namespace KitX.Core.Workflow.BlockScripting.BuiltinFunctions
{
    public class WriteTextFileFunction : IBuiltinFunctionDefinition
    {
        public string FunctionName => "WriteTextFile";
        public string DisplayName => "Write Text File";
        public bool IsFlowControl => false;
        public bool IsNonExtractable => true;
        public BlueprintNodeType? LegacyNodeType => null;
        public CFGStatementKind StatementKind => CFGStatementKind.Expression;
        public double NodeWidth => 160;
        public double NodeHeight => 80;

        public IReadOnlyList<PinDescriptor> InputPins => [
            new("Exec", PinType.Execution, 20),
            new("Path", PinType.String, 35),
            new("Content", PinType.String, 35)
        ];

        public IReadOnlyList<PinDescriptor> OutputPins => [
            new("Exec", PinType.Execution, 20)
        ];

        public BlockStatement? ExtractStatement(InvocationExpressionSyntax invoke, int lineNumber, string? exprText) => null;

        public List<FormattedStatement> FormatInvocation(
            InvocationExpressionSyntax invoke, string blockName,
            PipelineContext context, string? assignedVar)
        {
            var args = invoke.ArgumentList.Arguments.Select(a => a.Expression.ToString()).ToList();
            return [new FormattedStatement
            {
                BlockName = blockName,
                Kind = CFGStatementKind.Expression,
                FunctionName = FunctionName,
                Arguments = args,
                OriginalExpression = invoke.ToString(),
                SourceLine = 0,
            }];
        }

        public BlueprintNode ConfigureNode(BlueprintNode node, FormattedStatement stmt) => node;

        public BlockStatement? ToStatement(BlueprintNode node, INodeExportHelper helper)
        {
            var path = helper.GetInputValue(node, "Path");
            var content = helper.GetInputValue(node, "Content");
            return new ExpressionStatement
            {
                Expression = $"{FunctionName}({path}, {content})",
                SourceCode = $"{FunctionName}({path}, {content});",
                LineNumber = 1
            };
        }

        public IEnumerable<OutputArmDescriptor> GetOutputArms() => [];
    }
}

namespace KitX.Core.Workflow.BlockScripting
{
    public partial class BlockScriptExecutionGlobals
    {
        public void WriteTextFile(string path, string content)
        {
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(path, content);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[BlockScriptGlobals] WriteTextFile failed for {Path}", path);
            }
        }
    }
}
