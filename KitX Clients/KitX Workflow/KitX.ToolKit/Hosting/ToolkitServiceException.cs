namespace KitX.ToolKit.Hosting;

/// <summary>
/// A precondition failure on a ToolKit service operation, carrying the stable code ADR-0007 D2
/// asks for.
/// </summary>
/// <remarks>
/// <para><b>Why an exception and not a result type.</b> ADR-0007 D2 originally prescribed
/// <c>Result&lt;T&gt;</c>. That type was removed from the contract because nothing on the contract
/// surface needed it, and reinstating it for three methods would have cost a contract type to
/// wrap values the host already handles with <c>try</c>/<c>catch</c>. What the ADR actually
/// requires is a <b>stable code</b> — the View protocol's <c>rejected{code,message}</c> cannot be
/// derived from a prose message. That requirement is met here, on the exception, instead of by
/// changing every signature.</para>
///
/// <para><b>Why the type is internal, and what that costs.</b> Hosts that only want to show a
/// message keep catching <see cref="InvalidOperationException"/> and reading <c>Message</c>;
/// nothing about them changes. The consumer that needs the <i>code</i> is the adapter that will
/// serialise it into the View protocol's <c>rejected{code,message}</c>. That adapter does not
/// exist yet, and when it does it may not live in <c>KitX.ToolKit.Hosting</c> — design §9.6 puts
/// the network server elsewhere. <b>So the cost of <c>internal</c> is: whoever writes that
/// adapter has to make this type visible first</b> (or copy the two constants, which is worse,
/// because then the codes have two homes). That is a decision to make then, with the adapter's
/// shape in hand, not a limitation to hide now.</para>
///
/// <para><b>These codes are frozen once shipped</b>, exactly like the compile-time diagnostics —
/// renaming one breaks every consumer that branched on it, silently.</para>
/// </remarks>
internal sealed class ToolkitServiceException : InvalidOperationException
{
    /// <summary>An operation that requires the toolkit to be unmounted ran while it was mounted.</summary>
    public const string MountedCode = "toolkit.mounted";

    /// <summary>The requested toolkit has no config on disk.</summary>
    public const string NotFoundCode = "toolkit.not-found";

    public ToolkitServiceException(string code, string message) : base(message)
        => Code = code;

    /// <summary>The stable code; safe to branch on and safe to serialise.</summary>
    public string Code { get; }
}
