using KitX.ToolKit.Models;
using KitX.ToolKit.Validation;

namespace KitX.Host.Perf.Benchmarks;

/// <summary>
/// G5 · ConfigValidator 单次校验：合成 280 工作流 Toolkit（内存构造，工作流 Id/文件字段填充，
/// 形态参照 ToolKit.Test.Xunit.ConfigTests），跑 <c>Validate()</c> 10 次计时。指标：ms/次。
/// </summary>
public static class ConfigValidatorBenchmark
{
    private const int WorkflowCount = 280;

    public static List<string> Run()
    {
        var toolkit = BuildToolkit();
        var validator = new ConfigValidator();

        // 行为断言：合成 config 应校验通过（防测空/防误报）。
        var result = validator.Validate(toolkit);
        if (!result.IsValid)
            throw new InvalidOperationException("合成 config 校验未通过: " + string.Join("; ", result.Errors));

        var m = Benchmark.Measure(3, 10, () => validator.Validate(toolkit));

        return
        [
            m.Row("G5 ConfigValidator 单次校验", $"{WorkflowCount} 工作流合成 config，× 10 次"),
        ];
    }

    private static Toolkit BuildToolkit()
    {
        var workflows = new List<ToolkitWorkflow>(WorkflowCount);
        for (var i = 0; i < WorkflowCount; i++)
            workflows.Add(new ToolkitWorkflow { Id = $"wf-{i}", Name = $"Workflow {i}", File = $"workflows/wf-{i}.kcs" });

        var triggers = new List<Trigger>();
        for (var i = 0; i < 20; i++)
        {
            triggers.Add(new Trigger
            {
                Id = $"trg-plugin-{i}",
                Type = TriggerType.PluginEvent,
                Config = new TriggerConfig { PluginName = "KitX.Plugin." + i, TriggerName = "Event" + i },
                Bindings = [new TriggerBinding { Workflow = $"wf-{i}" }],
            });
            triggers.Add(new Trigger
            {
                Id = $"trg-timer-{i}",
                Type = TriggerType.Timer,
                Config = new TriggerConfig { IntervalMs = 1000 },
                Bindings = [new TriggerBinding { Workflow = $"wf-{i + 20}" }],
            });
            triggers.Add(new Trigger
            {
                Id = $"trg-completion-{i}",
                Type = TriggerType.WorkflowCompletion,
                Config = new TriggerConfig { From = $"wf-{i}" },
                Bindings = [new TriggerBinding { Workflow = $"wf-{i + 40}" }],
            });
            triggers.Add(new Trigger
            {
                Id = $"trg-ui-{i}",
                Type = TriggerType.UIEvent,
                Config = new TriggerConfig { Control = $"btn-{i}", Event = "Click" },
                Bindings = [new TriggerBinding { Workflow = $"wf-{i + 60}" }],
            });
        }

        var controls = new List<UiControl>();
        for (var i = 0; i < 20; i++)
        {
            controls.Add(new UiControl { Type = "Button", Id = $"btn-{i}" });
            controls.Add(new UiControl { Type = "Input", Id = $"input-{i}", Bind = $"panel/input-{i}/value" });
        }

        return new Toolkit
        {
            Id = "perf-toolkit",
            Meta = new ToolkitMeta { Name = "Perf Toolkit", Version = "1.0.0" },
            Workflows = workflows,
            Triggers = triggers,
            UiPanel = new UiPanel { Controls = controls },
        };
    }
}
