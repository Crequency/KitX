namespace KitX.ToolKit.Contract.Declaration;

/// <summary>
/// One entry in a ToolKit's key table (ADR-0012 D3, design §5.1).
///
/// <para><b>Why a declared table at all.</b> The data plane used to be "any string key": a workflow
/// could write a name nobody read, and nothing could enumerate what a ToolKit actually touches.
/// Declaring the keys turns the data plane from an open string space into a table, which is also
/// the only thing a region can still be derived from now that the control list is gone.</para>
///
/// <para>A workflow writing an undeclared key is a load-time error, not a silent write into a key
/// nobody reads.</para>
/// </summary>
/// <param name="Name">The key name as workflows write it — always local, never prefixed. The
/// system builds the full key, so "read another ToolKit's data" is not expressible
/// (ADR-0004 D2).</param>
/// <param name="Type">A JSON type hint for the View (<c>"string"</c>, <c>"number"</c>,
/// <c>"boolean"</c>, <c>"object"</c>, <c>"array"</c>). Advisory: the store does not enforce it,
/// because a workflow's JSON value is what it is.</param>
/// <param name="Role">Who may write it. See <see cref="KeyRole"/>.</param>
/// <param name="Region">
/// Optional grouping name (design §2.1). Regions are the unit of cleanup and of "what the View
/// asks for" — a View that wants one screen's worth of state asks for a region, not for every key
/// by name. Null means the key is not grouped.
/// </param>
public sealed record KeyDeclaration(string Name, string Type, KeyRole Role, string? Region = null);
