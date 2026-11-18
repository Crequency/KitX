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

    /// <summary>
    /// How long one line may take. Not a performance budget — a runaway guard.
    /// </summary>
    /// <remarks>
    /// The five lines below tokenize in well under a millisecond each; this is a hundredfold
    /// margin chosen so a pathological line cannot hang the suite. Do not read it as "the line
    /// needs 100ms".
    /// </remarks>
    private static readonly TimeSpan LineBudget = TimeSpan.FromMilliseconds(100);

    /// <summary>Tokenizes a single line and returns (text → last scope) pairs.</summary>
    /// <remarks>
    /// <b>A short result is a truncated line, and it used to read as a wrong grammar.</b>
    /// <c>TokenizeLine</c> returns whatever it managed to tokenize when the budget runs out,
    /// without saying so: no exception, no flag, just fewer tokens. The assertions below index a
    /// dictionary, so a line cut off before its last word surfaced as
    /// <c>KeyNotFoundException: 'cond'</c> — which reads as "the grammar does not scope this",
    /// the one thing it does not mean. Measured with a 12000-character line: at a 1ms budget it
    /// returns 5 tokens covering 21 of 12049 characters and reports nothing.
    ///
    /// <para><b>So completeness is checked first, and it fails with a sentence about the
    /// tokenizer.</b> The check must be <c>&gt;=</c>, not <c>==</c>: <c>EndIndex</c> overshoots
    /// the line by one on a line it finished (measured: 65 for a 64-character line, 6 for 5),
    /// because it counts the terminating position.</para>
    /// </remarks>
    private static List<(string Text, string Scope)> Tokenize(IGrammar grammar, string line)
    {
        var result = grammar.TokenizeLine(new LineText(line), null, LineBudget);
        var tokens = (result.Tokens ?? []).ToList();

        var covered = tokens.Count == 0 ? 0 : tokens[^1].EndIndex;

        Assert.True(covered >= line.Length,
            $"the tokenizer stopped at {covered} of {line.Length} characters on \"{line}\": " +
            "a truncated line, not a grammar that scopes fewer things");

        var pairs = new List<(string, string)>();
        int pos = 0;
        foreach (var t in tokens)
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
