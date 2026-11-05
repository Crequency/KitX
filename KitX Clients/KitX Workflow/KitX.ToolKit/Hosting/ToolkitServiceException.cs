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
/// <para><b>Why the type is internal.</b> The only consumer that needs the code is the adapter
/// that will serialise it into the View protocol, and that adapter lives in
/// <c>KitX.ToolKit.Hosting</c>, which already sees implementation internals. Hosts that only want
/// to show a message keep catching <see cref="InvalidOperationException"/> and reading
/// <c>Message</c>; nothing about them changes.</para>
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
