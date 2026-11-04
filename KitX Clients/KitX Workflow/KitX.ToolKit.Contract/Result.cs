namespace KitX.ToolKit.Contract;

/// <summary>
/// The result of a contract operation that can fail in expected ways (ADR-0007 D2).
///
/// <para><b>Exceptions are for things that should not happen</b> (null dereference,
/// impossible branches). "Cannot update while mounted" is an expected precondition
/// failure, not an exception — and an exception message cannot be expressed in a
/// pure-network contract, which is why this type exists.</para>
///
/// <para>Invariant: <see cref="Error"/> is null exactly when the operation succeeded.
/// A <i>successful</i> result may still carry a null <see cref="Value"/> for reference
/// types — "not found" is expressed as success-with-null, never as a failure. Use
/// <see cref="Succeeded"/> to branch; use <see cref="Diagnostic.Code"/> to decide
/// <i>which</i> failure it was.</para>
/// </summary>
/// <typeparam name="T">The produced value.</typeparam>
public readonly record struct Result<T>(T? Value, Diagnostic? Error)
{
    /// <summary>True when there is no error. A successful result may legitimately carry a
    /// null value for reference types (e.g. "not found" expressed as success-with-null).</summary>
    public bool Succeeded => Error is null;

    /// <summary>Builds a successful result.</summary>
    public static Result<T> Ok(T value) => new(value, null);

    /// <summary>Builds a failed result.</summary>
    public static Result<T> Fail(Diagnostic error) => new(default, error);
}
