// ─────────────────────────────────────────────────────────────────────────────
// KS TextMate grammar tests — the grammar (Assets/TextMate/ks) drives syntax
// highlighting in the v6 editor. These verify that every pipeline element
// (variables, functions incl. append form, pipe operator, placeholders) gets a
// dedicated scope, so the custom theme can colour it.
// ─────────────────────────────────────────────────────────────────────────────

using TextMateSharp.Grammars;
using TextMateSharp.Registry;
using Xunit;

namespace KitX.WorkflowV6.Test.Xunit;

[Trait("Category", "Unit")]
public class KsGrammarTests
{
    private static string GrammarDir => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
        "..", "..", "..", "..", "..",
        "KitX Dashboard", "KitX Dashboard", "Assets", "TextMate", "ks"));

    /// <summary>Tokenizes a single line and returns (text → last scope) pairs.</summary>
    private static List<(string Text, string Scope)> Tokenize(IGrammar grammar, string line)
    {
        var result = grammar.TokenizeLine(new LineText(line), null, TimeSpan.FromMilliseconds(100));
        var pairs = new List<(string, string)>();
        int pos = 0;
        foreach (var t in result.Tokens ?? [])
        {
            int end = Math.Min(t.EndIndex, line.Length);
            if (end <= pos) continue;
            var text = line.Substring(pos, end - pos);
            pos = end;
            if (t.Scopes.Count > 1)
                pairs.Add((text, t.Scopes[^1]));
        }
        return pairs;
    }

    [Fact]
    public void Grammar_Scopes_Variables_Functions_Pipe_Placeholders()
    {
        var options = new RegistryOptions(ThemeName.DarkPlus);
        options.LoadFromLocalFile("ks", new FileInfo(Path.Combine(GrammarDir, "package.json")), overwrite: true);
        var registry = new Registry(options);
        var grammar = registry.LoadGrammar(options.GetScopeByLanguageId("ks"));
        Assert.NotNull(grammar);

        var scopes = new Dictionary<string, string>();
        foreach (var line in new[]
        {
            "guessNum, targetNum > Compare(\"BEQ\", _, _) > cond",
            "memorySize > CreateMemory > memory",
            "vaaa0001 > PluginCall(\"TestPlugin.WPF.Core\", \"HelloAnything\", _)",
            "var {",
            "    int counter = 0",
        })
        {
            foreach (var (text, scope) in Tokenize(grammar, line))
                scopes[text.Trim()] = scope;
        }

        // Variables / constants get a dedicated scope (previously plain black).
        Assert.Equal("variable.other.ks", scopes["guessNum"]);
        Assert.Equal("variable.other.ks", scopes["cond"]);
        Assert.Equal("variable.other.ks", scopes["memory"]);
        Assert.Equal("variable.other.ks", scopes["vaaa0001"]);
        Assert.Equal("variable.other.ks", scopes["counter"]);
        // Pipe operator.
        Assert.Equal("keyword.operator.pipe.ks", scopes[">"]);
        // Builtin / helper calls, INCLUDING append form without parens (`> CreateMemory`).
        Assert.Equal("support.function.ks", scopes["Compare"]);
        Assert.Equal("support.function.ks", scopes["PluginCall"]);
        Assert.Equal("support.function.ks", scopes["CreateMemory"]);
        // Keywords / types / placeholders.
        Assert.Equal("keyword.control.ks", scopes["var"]);
        Assert.Equal("storage.type.ks", scopes["int"]);
        Assert.Equal("variable.parameter.placeholder.ks", scopes["_"]);
    }
}
