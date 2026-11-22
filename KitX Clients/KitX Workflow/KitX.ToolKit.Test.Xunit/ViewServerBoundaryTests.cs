using System.Reflection;
using KitX.ToolKit.View.Server;
using Xunit;

namespace KitX.ToolKit.Test.Xunit;

/// <summary>
/// The transport may reference the contract and nothing beneath it — and this is the assertion the
/// transport's own remarks had been citing for a day before it existed.
/// </summary>
/// <remarks>
/// <para><b>Why the metadata and not the project file.</b> Same reason as
/// <c>ProductBoundaryTests</c>: a <c>ProjectReference</c> is what somebody declared, while an
/// assembly reference in metadata is what the compiler actually bound to. A project can reach a
/// transitive dependency and only the output shows it.</para>
///
/// <para><b>What it would catch.</b> A <see cref="ViewServer"/> or <see cref="SocketChannel"/> that
/// called <c>DataStore</c>, the instance manager or a <c>ToolkitStore</c> — any of which would let
/// the transport answer a question the protocol never asked, and would make the two ends of the wire
/// depend on one particular server implementation.</para>
///
/// <para><b>Its limit, stated so nobody trusts it further than it goes.</b> It proves no
/// implementation <em>type</em> was bound against. A service resolved as <see langword="object"/>
/// and used by reflection leaves no trace, exactly as the host's boundary test says of itself.</para>
/// </remarks>
[Trait("Category", "Unit")]
public sealed class ViewServerBoundaryTests
{
    /// <summary>The assembly a transport must never compile against.</summary>
    private const string Implementation = "KitX.ToolKit";

    [Fact]
    public void The_transport_does_not_reference_the_implementation_assembly()
    {
        var transport = typeof(ViewServer).Assembly;

        var referenced = transport.GetReferencedAssemblies()
            .Select(a => a.Name)
            .ToArray();

        Assert.DoesNotContain(Implementation, referenced);

        // Named so a failure distinguishes "absent because bounded" from "absent because the
        // assembly could not be loaded at all".
        Assert.Contains("KitX.ToolKit.Contract", referenced);
    }

    [Fact]
    public void The_client_half_of_the_envelope_is_in_the_same_assembly_as_the_server_half()
    {
        // Not a style preference: two assemblies holding one format is how the two constants drifted
        // in the first place. If this ever fails, the wire has two declarations again.
        Assert.Same(typeof(ViewServer).Assembly, typeof(SocketChannel).Assembly);
    }

    [Fact]
    public void The_reference_table_is_not_silently_empty()
    {
        // The assertion above would pass for a trimmed build, a wrong overload, or a moved property
        // — all of which produce an empty list rather than a failure.
        var referenced = typeof(ViewServer).Assembly.GetReferencedAssemblies();

        Assert.NotEmpty(referenced);
        Assert.Contains("System.Runtime", referenced.Select(a => a.Name));
    }
}
