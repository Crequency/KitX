using KitX.ToolKit.Models;
using KitX.ToolKit.Validation;
using Xunit;

namespace KitX.ToolKit.Test.Xunit;

[Trait("Category", "Unit")]
public class ConfigTests
{
    private static Toolkit Sample()
    {
        return new Toolkit
        {
            Meta = new ToolkitMeta { Name = "AI Assistant Toolkit", Version = "1.0.0", MinKitXVersion = "3.25.4.0" },
            Workflows =
            [
                new ToolkitWorkflow { Id = "wf-init", Name = "初始化", File = "workflows/init.kcs" },
                new ToolkitWorkflow { Id = "wf-trans", Name = "翻译", File = "workflows/translate.kcs" },
            ],
            Plugins = [new PluginRequirement { Name = "KitX.AI.Plugin", Version = ">=1.0.0", Source = "local" }],
            Triggers =
            [
                new Trigger
                {
                    Id = "trg-plugin",
                    Type = TriggerType.PluginEvent,
                    Config = new TriggerConfig { PluginName = "KitX.AI.Plugin", TriggerName = "TranslateRequest" },
                    Bindings = [new TriggerBinding { Workflow = "wf-trans", Params = new() { ["text"] = "$payload.content" } }],
                },
                new Trigger
                {
                    Id = "trg-init-done",
                    Type = TriggerType.WorkflowCompletion,
                    Config = new TriggerConfig { From = "wf-init" },
                    Bindings = [new TriggerBinding { Workflow = "wf-trans", Params = new() { ["model"] = "$output.model" } }],
                },
            ],
        };
    }

    [Fact]
    public void Serialize_Deserialize_RoundTrips_Config()
    {
        var json = ToolkitConfig.Serialize(Sample());

        var back = ToolkitConfig.Deserialize(json);

        Assert.NotNull(back);
        Assert.Equal("AI Assistant Toolkit", back.Meta.Name);
        Assert.Equal(2, back.Workflows.Count);
        Assert.Equal(2, back.Triggers.Count);
        Assert.Equal(TriggerType.PluginEvent, back.Triggers[0].Type);
        Assert.Equal("KitX.AI.Plugin", back.Triggers[0].Config.PluginName);
        Assert.Equal("$payload.content", back.Triggers[0].Bindings[0].Params["text"]);
    }

    [Fact]
    public void Deserialize_Handles_Comments_And_TrailingCommas()
    {
        const string json = """
        {
          "Name": "Demo",
          "Version": "1.0.0",
          "Workflows": [
            { "Id": "a", "Name": "A", "File": "a.kcs" },   // trailing comment
          ],
          "Triggers": [],
        }
        """;

        var toolkit = ToolkitConfig.Deserialize(json);

        Assert.NotNull(toolkit);
        Assert.Single(toolkit.Workflows);
        Assert.Empty(toolkit.Triggers);
    }

    [Fact]
    public void Validate_Accepts_Valid_Config()
    {
        var result = ToolkitConfig.Validate(Sample());
        Assert.True(result.IsValid, string.Join("; ", result.Errors));
    }

    [Fact]
    public void Validate_Rejects_Dangling_Binding()
    {
        var tk = Sample();
        tk.Triggers[0].Bindings[0].Workflow = "missing-wf";

        var result = new ConfigValidator().Validate(tk);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("missing-wf"));
    }

    [Fact]
    public void Validate_Rejects_Cycle()
    {
        var tk = new Toolkit
        {
            Workflows =
            [
                new ToolkitWorkflow { Id = "A", Name = "A", File = "a.kcs" },
                new ToolkitWorkflow { Id = "B", Name = "B", File = "b.kcs" },
            ],
            Triggers =
            [
                new Trigger { Id = "e1", Type = TriggerType.WorkflowCompletion, Config = new() { From = "A" },
                    Bindings = [new TriggerBinding { Workflow = "B" }] },
                new Trigger { Id = "e2", Type = TriggerType.WorkflowCompletion, Config = new() { From = "B" },
                    Bindings = [new TriggerBinding { Workflow = "A" }] },
            ],
        };

        var result = new ConfigValidator().Validate(tk);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("cycle", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_Rejects_Duplicate_Ids()
    {
        var tk = Sample();
        tk.Workflows[1].Id = tk.Workflows[0].Id;

        var result = new ConfigValidator().Validate(tk);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_Rejects_Bind_Outside_Panel_Namespace()
    {
        var tk = Sample();
        tk.UiPanel = new UiPanel
        {
            Controls = [new UiControl { Type = "Input", Id = "input", Bind = "wf/input/value" }],
        };

        var result = new ConfigValidator().Validate(tk);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("panel/"));
    }

    [Fact]
    public void Validate_Rejects_UIEvent_Unknown_Control()
    {
        var tk = Sample();
        tk.UiPanel = new UiPanel
        {
            Controls = [new UiControl { Type = "Button", Id = "btn" }],
        };
        tk.Triggers.Add(new Trigger
        {
            Id = "trg-ui",
            Type = TriggerType.UIEvent,
            Config = new TriggerConfig { Control = "missing", Event = "Click" },
            Bindings = [new TriggerBinding { Workflow = "wf-trans" }],
        });

        var result = new ConfigValidator().Validate(tk);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("missing"));
    }

    [Fact]
    public void Validate_Rejects_Bind_Aliasing_Dialog_Request_Key()
    {
        var tk = Sample();
        tk.UiPanel = new UiPanel
        {
            Controls =
            [
                new UiControl { Type = "Dialog", Id = "dlg" },
                new UiControl { Type = "Text", Id = "lbl", Bind = "panel/dlg/request" },
            ],
        };

        var result = new ConfigValidator().Validate(tk);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("aliases Dialog"));
    }

    [Fact]
    public void Validate_Rejects_Unknown_Control_Type()
    {
        var tk = Sample();
        tk.UiPanel = new UiPanel
        {
            Controls = [new UiControl { Type = "NotARealControl", Id = "bad" }],
        };

        var result = new ConfigValidator().Validate(tk);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("unknown type 'NotARealControl'"));
    }

    [Fact]
    public void Validate_Rejects_PeriodicTimer_WithoutInterval()
    {
        var tk = Sample();
        tk.Triggers.Add(new Trigger
        {
            Id = "timer-bad",
            Type = TriggerType.Timer,
            Config = new TriggerConfig(),
        });

        var result = new ConfigValidator().Validate(tk);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("positive IntervalMs"));
    }

    [Fact]
    public void Validate_Rejects_NegativeMaxInstances()
    {
        var tk = Sample();
        tk.MaxInstances = -1;

        var result = new ConfigValidator().Validate(tk);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("MaxInstances"));
    }
}
