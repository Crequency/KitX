using KitX.ToolKit.Compile;
using KitX.ToolKit.Contract.Declaration;

using Xunit;

namespace KitX.ToolKit.Test.Xunit;

/// <summary>
/// Compiles a config the way the host does before it can build a scheduler.
/// </summary>
/// <remarks>
/// <para>Mounting a toolkit is now validate → compile → schedule, so a test that wants a
/// <see cref="Bench.RunScheduler"/> has to go through the same gate. This helper exists so
/// that requirement costs each test one line instead of repeating the diagnostic formatting
/// five times — and so a compile failure surfaces as the compiler's own message rather than as
/// a confusing assertion further down.</para>
/// </remarks>
internal static class CompileFixtures
{
    /// <summary>
    /// Compiles <paramref name="toolkit"/>, failing the test with every diagnostic if it does not
    /// compile. Pass <paramref name="storageRoot"/> only when the test cares about resolved file
    /// paths; the default resolves nothing, which is enough for scheduling behaviour.
    /// </summary>
    public static CompiledToolkit Compile(Toolkit toolkit, string? storageRoot = null)
    {
        var compiled = ToolkitCompiler.Compile(toolkit, out var diagnostics, storageRoot: storageRoot);

        Assert.True(
            compiled is not null,
            "config did not compile:\n  " + string.Join("\n  ", diagnostics.Select(d => $"[{d.Code}] {d.Message}")));

        return compiled!;
    }
}
