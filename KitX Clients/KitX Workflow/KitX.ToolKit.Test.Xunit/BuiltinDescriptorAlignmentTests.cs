using System.Reflection;
using KitX.ToolKit.Builtin;
using KitX.ToolKit.Builtin.Functions;
using KitX.WorkflowV6.Builtin;
using Xunit;

namespace KitX.ToolKit.Test.Xunit;

/// <summary>
/// ADR-0011 D4: every name in the descriptor registry must resolve to a method of the same name
/// on <see cref="ToolKitExecutionGlobals"/>.
/// </summary>
/// <remarks>
/// <para>The failure this catches is silent by nature. Codegen asks
/// <c>BuiltinRuntimeTypes.Resolve</c> for a builtin's return type by reflecting over the globals
/// base; when the method is missing it gets <c>null</c> and keeps emitting. The workflow then fails
/// to compile — or worse, compiles to something whose type is wrong — at a location that has
/// nothing to do with the missing method.</para>
/// <para>This test is the whole of ADR-0011 D4 and it is deliberately exhaustive: it walks the
/// registry, not a hand-written sample, so a new descriptor cannot skip it.</para>
/// </remarks>
[Trait("Category", "Unit")]
public class BuiltinDescriptorAlignmentTests
{
    /// <summary>The methods the generated workflow code can call, by name.</summary>
    private static readonly Dictionary<string, MethodInfo[]> GlobalsMethods =
        typeof(ToolKitExecutionGlobals)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .GroupBy(m => m.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);

    [Fact]
    public void Every_descriptor_name_has_a_matching_method_on_the_execution_globals()
    {
        var missing = BuiltinRegistry.Names
            .Where(name => !GlobalsMethods.ContainsKey(name))
            .ToList();

        Assert.True(missing.Count == 0,
            "These descriptors have no method of the same name on " +
            $"{nameof(ToolKitExecutionGlobals)}: {string.Join(", ", missing)}. " +
            "A registered builtin without an implementation fails at codegen, far from here.");
    }

    [Fact]
    public void Every_descriptor_name_is_unique()
    {
        // The registry is a list of types; two types sharing a Name would silently register the
        // same key twice, and the second would win with no diagnostic anywhere.
        var duplicates = BuiltinRegistry.Names
            .GroupBy(n => n, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.True(duplicates.Count == 0, $"Duplicate builtin names: {string.Join(", ", duplicates)}");
    }

    [Fact]
    public void Every_descriptor_is_a_concrete_builtin_with_a_public_parameterless_constructor()
    {
        // Both registration and the name lookup above instantiate through Activator, so a
        // descriptor that needs constructor arguments would throw at DI build time instead.
        var offenders = BuiltinRegistry.All
            .Where(t => t.IsAbstract || t.IsInterface
                        || !typeof(IBuiltinFunction).IsAssignableFrom(t)
                        || t.GetConstructor(Type.EmptyTypes) is null)
            .Select(t => t.FullName)
            .ToList();

        Assert.True(offenders.Count == 0,
            $"Descriptors must be concrete {nameof(IBuiltinFunction)} types with a parameterless " +
            $"constructor: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void Every_public_globals_method_that_looks_like_a_builtin_is_registered()
    {
        // The other direction. A method with a builtin-shaped name that nobody registered is dead
        // code at best, and at worst a function the author believes is callable from KS. The
        // Ui*/Toolkit*/Bench* prefix set is what makes "looks like a builtin" decidable.
        var builtinShaped = GlobalsMethods.Keys
            .Where(name => name.StartsWith("Ui", StringComparison.Ordinal)
                        || name.StartsWith("Toolkit", StringComparison.Ordinal)
                        || name.StartsWith("Bench", StringComparison.Ordinal))
            .ToList();

        var registered = BuiltinRegistry.Names.ToHashSet(StringComparer.Ordinal);
        var unregistered = builtinShaped.Where(n => !registered.Contains(n)).ToList();

        Assert.True(unregistered.Count == 0,
            $"These {nameof(ToolKitExecutionGlobals)} methods are builtin-shaped but not in the " +
            $"registry: {string.Join(", ", unregistered)}");
    }
}
