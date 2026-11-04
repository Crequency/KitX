namespace KitX.ToolKit.Time;

/// <summary>
/// The only source of generated identifiers in this assembly (ADR-0006 D1). Injected through
/// constructors, never resolved from a service locator (ADR-0006 D2).
/// </summary>
/// <remarks>
/// <para>Two methods rather than one <c>NewId()</c> because the two kinds of identifier have
/// different lifecycles: a flow id is never durable (ADR-0006 D3 — it is a correlation key and
/// explicitly <b>not</b> an address), while a ToolKit id is written into a config file and lives
/// as long as that file does. One method would make the two look interchangeable.</para>
/// <para>There is deliberately no <c>IEnumerable&lt;string&gt;</c> query and no
/// <c>Cancel(string flowId)</c> anywhere near this seam: ADR-0006 D3 forbids flow ids from
/// becoming addresses, and the cheapest way to honour that is to never give code a way to
/// enumerate them.</para>
/// </remarks>
internal interface IIdGenerator
{
    /// <summary>A fresh id for one flow execution. Correlation only — never an address.</summary>
    string NewFlowId();

    /// <summary>A fresh default id for a newly created ToolKit config (ADR-0006 D2).</summary>
    string NewToolkitId();
}
