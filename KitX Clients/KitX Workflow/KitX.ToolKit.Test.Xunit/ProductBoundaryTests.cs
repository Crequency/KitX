using System.Reflection;
using KitX.ToolKit.View.Avalonia.Host;
using Xunit;

namespace KitX.ToolKit.Test.Xunit;

/// <summary>
/// The host must not be able to reach past the protocol, and this is the assertion that says so.
/// </summary>
/// <remarks>
/// <para><b>Why not check the <c>.csproj</c>.</b> Scanning project files for a forbidden
/// <c>ProjectReference</c> reads what someone *declared*. Measured on this repository: a project
/// that references only <c>KitX.ToolKit.Hosting</c> compiles
/// <c>new KitX.ToolKit.Builtin.ToolKitExecutionGlobals()</c> happily, because that reference is
/// transitive and carries no <c>PrivateAssets</c>. A csproj scan would stay green through exactly
/// the violation it was written to catch.</para>
///
/// <para><b>What is checked instead, and why it has discriminating power.</b> The C# compiler writes
/// an assembly reference into metadata only when the assembly actually used something from it.
/// Measured: a project that references a middle library but only calls its public types gets
/// metadata naming that middle library alone; adding one line that touches the library beneath it
/// makes that second name appear. So "no <c>KitX.ToolKit</c> in the reference table" means "no type
/// from the implementation was compiled against" — an observation of the output rather than of the
/// intent.</para>
///
/// <para><b>The limit of this assertion, stated so nobody trusts it further than it goes.</b> It
/// proves the host did not bind to an implementation <em>type</em>. It does not prove the host never
/// touches the implementation: a service resolved as <see langword="object"/> and used through
/// reflection, or an instance obtained from the container and passed on, leaves no metadata trace.
/// The wire-level rule this enforces is narrower than "the host is sandboxed", and calling it
/// sandboxing would be the kind of claim a future reader would rely on wrongly.</para>
/// </remarks>
[Trait("Category", "Unit")]
public class ProductBoundaryTests
{
    /// <summary>The assembly a View must never compile against (ADR-0014 D5, ADR-0001).</summary>
    private const string Implementation = "KitX.ToolKit";

    [Fact]
    public void The_host_assembly_does_not_reference_the_implementation_assembly()
    {
        var host = typeof(HostSession).Assembly;

        var referenced = host.GetReferencedAssemblies()
            .Select(a => a.Name)
            .ToArray();

        Assert.DoesNotContain(Implementation, referenced);

        // Named for the failure message's sake: a bare DoesNotContain would report only that the
        // name is absent, which is also true of an assembly that could not be loaded at all.
        Assert.Contains("KitX.ToolKit.Contract", referenced);
        Assert.Contains("KitX.ToolKit.View", referenced);
    }

    /// <summary>
    /// The reverse of the assertion above would be vacuous, so this pins that it is not.
    /// </summary>
    /// <remarks>
    /// If <see cref="Assembly.GetReferencedAssemblies"/> returned an empty list for everything — a
    /// trimmmed build, a wrong overload, a moved property — the test above would pass for a reason
    /// that has nothing to do with the boundary. This one fails in that world.
    /// </remarks>
    [Fact]
    public void The_reference_table_is_not_silently_empty()
    {
        var referenced = typeof(HostSession).Assembly.GetReferencedAssemblies();

        Assert.NotEmpty(referenced);
        Assert.Contains("System.Runtime", referenced.Select(a => a.Name));
    }
}
