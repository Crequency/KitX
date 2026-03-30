using System;
using System.Collections.Generic;
using KitX.Core.Contract.Workflow;

namespace KitX.Core.Workflow.Blueprint;

/// <summary>
/// 节点模板提供者 - 统一所有节点的尺寸和引脚位置
/// </summary>
public class NodeTemplateProvider : INodeTemplateProvider
{
    private readonly Dictionary<BlueprintNodeType, NodeTemplate> _templates;

    public NodeTemplateProvider()
    {
        _templates = new Dictionary<BlueprintNodeType, NodeTemplate>
        {
            [BlueprintNodeType.Entry] = new NodeTemplate
            {
                NodeType = BlueprintNodeType.Entry,
                Name = "Entry",
                Width = 120,
                Height = 60,
                OutputPins =
                [
                    new PinTemplate { Name = "Exec", Type = PinType.Execution, RelativeY = 30 }
                ]
            },

            [BlueprintNodeType.Branch] = new NodeTemplate
            {
                NodeType = BlueprintNodeType.Branch,
                Name = "Branch",
                Width = 120,
                Height = 80,
                InputPins =
                [
                    new PinTemplate { Name = "Exec", Type = PinType.Execution, RelativeY = 30 },
                    new PinTemplate { Name = "Condition", Type = PinType.Boolean, RelativeY = 50 }
                ],
                OutputPins =
                [
                    new PinTemplate { Name = "True", Type = PinType.Execution, RelativeY = 30 },
                    new PinTemplate { Name = "False", Type = PinType.Execution, RelativeY = 50 }
                ]
            },

            [BlueprintNodeType.Loop] = new NodeTemplate
            {
                NodeType = BlueprintNodeType.Loop,
                Name = "Loop",
                Width = 120,
                Height = 80,
                InputPins =
                [
                    new PinTemplate { Name = "Exec", Type = PinType.Execution, RelativeY = 30 },
                    new PinTemplate { Name = "Condition", Type = PinType.Boolean, RelativeY = 50 }
                ],
                OutputPins =
                [
                    new PinTemplate { Name = "LoopBody", Type = PinType.Execution, RelativeY = 30 },
                    new PinTemplate { Name = "LoopEnd", Type = PinType.Execution, RelativeY = 50 }
                ]
            },

            [BlueprintNodeType.Break] = new NodeTemplate
            {
                NodeType = BlueprintNodeType.Break,
                Name = "Break",
                Width = 100,
                Height = 40,
                InputPins =
                [
                    new PinTemplate { Name = "Exec", Type = PinType.Execution, RelativeY = 20 }
                ]
            },

            [BlueprintNodeType.Const] = new NodeTemplate
            {
                NodeType = BlueprintNodeType.Const,
                Name = "Const",
                Width = 120,
                Height = 50,
                OutputPins =
                [
                    new PinTemplate { Name = "Value", Type = PinType.Any, RelativeY = 25 }
                ]
            },

            [BlueprintNodeType.Call] = new NodeTemplate
            {
                NodeType = BlueprintNodeType.Call,
                Name = "Call",
                Width = 140,
                Height = 60,
                InputPins =
                [
                    new PinTemplate { Name = "Exec", Type = PinType.Execution, RelativeY = 20 }
                ],
                OutputPins =
                [
                    new PinTemplate { Name = "Exec", Type = PinType.Execution, RelativeY = 20 },
                    new PinTemplate { Name = "Return", Type = PinType.Any, RelativeY = 40 }
                ]
            },

            [BlueprintNodeType.CallHelper] = new NodeTemplate
            {
                NodeType = BlueprintNodeType.CallHelper,
                Name = "CallHelper",
                Width = 130,
                Height = 50,
                InputPins =
                [
                    new PinTemplate { Name = "Exec", Type = PinType.Execution, RelativeY = 25 }
                ],
                OutputPins =
                [
                    new PinTemplate { Name = "Exec", Type = PinType.Execution, RelativeY = 25 },
                    new PinTemplate { Name = "Return", Type = PinType.Any, RelativeY = 40 }
                ]
            },

            [BlueprintNodeType.Print] = new NodeTemplate
            {
                NodeType = BlueprintNodeType.Print,
                Name = "Print",
                Width = 100,
                Height = 50,
                InputPins =
                [
                    new PinTemplate { Name = "Exec", Type = PinType.Execution, RelativeY = 20 },
                    new PinTemplate { Name = "Value", Type = PinType.Any, RelativeY = 35 }
                ],
                OutputPins =
                [
                    new PinTemplate { Name = "Exec", Type = PinType.Execution, RelativeY = 25 }
                ]
            },

            [BlueprintNodeType.Pause] = new NodeTemplate
            {
                NodeType = BlueprintNodeType.Pause,
                Name = "Pause",
                Width = 100,
                Height = 50,
                InputPins =
                [
                    new PinTemplate { Name = "Exec", Type = PinType.Execution, RelativeY = 20 },
                    new PinTemplate { Name = "Milliseconds", Type = PinType.Integer, RelativeY = 35 }
                ],
                OutputPins =
                [
                    new PinTemplate { Name = "Exec", Type = PinType.Execution, RelativeY = 25 }
                ]
            }
        };
    }

    public BlueprintNode CreateNode(BlueprintNodeType type) => type switch
    {
        BlueprintNodeType.Entry => new EntryNode(),
        BlueprintNodeType.Branch => new BranchNode(),
        BlueprintNodeType.Loop => new LoopNode(),
        BlueprintNodeType.Break => new BreakNode(),
        BlueprintNodeType.Const => new ConstNode(),
        BlueprintNodeType.Call => new CallNode(),
        BlueprintNodeType.CallHelper => new CallHelperNode(),
        BlueprintNodeType.Print => new PrintNode(),
        BlueprintNodeType.Pause => new PauseNode(),
        _ => throw new ArgumentException($"Unknown node type: {type}")
    };

    public IReadOnlyDictionary<BlueprintNodeType, NodeTemplate> GetTemplates() => _templates;

    public (double Width, double Height) GetNodeSize(BlueprintNodeType type)
    {
        if (_templates.TryGetValue(type, out var template))
            return (template.Width, template.Height);
        return (120, 60);
    }

    public double GetInputPinY(BlueprintNodeType nodeType, string pinName)
    {
        if (_templates.TryGetValue(nodeType, out var template))
        {
            foreach (var pin in template.InputPins)
            {
                if (pin.Name == pinName)
                    return pin.RelativeY;
            }
        }
        return 30;
    }

    public double GetOutputPinY(BlueprintNodeType nodeType, string pinName)
    {
        if (_templates.TryGetValue(nodeType, out var template))
        {
            foreach (var pin in template.OutputPins)
            {
                if (pin.Name == pinName)
                    return pin.RelativeY;
            }
        }
        return 30;
    }
}
