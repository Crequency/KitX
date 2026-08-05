namespace KitX.WorkflowV6.Backend.Runtime;

// ─────────────────────────────────────────────────────────────────────────────
// ExecutionGlobals.Arithmetic — the numeric/string operator dispatchers
// (Compare / Add / Sub / Mul / Div / Mod / StringConcat / Range).
// Partial of ExecutionGlobals (see ExecutionGlobals.cs).
// ─────────────────────────────────────────────────────────────────────────────

public partial class ExecutionGlobals
{
    /// <summary>
    /// Compare dispatcher: compares a and b with the named operator.
    /// Op codes per §十二-B: BEQ/BNE/BLT/BLE/BGT/BGE.
    /// Integer operands use exact comparison (int→double is lossless within 2^53, but
    /// relative tolerance on large ints can falsely equate distinct values).
    /// Floating-point operands use a combined relative+absolute tolerance for equality
    /// (BEQ/BNE) to absorb IEEE-754 rounding; ordering comparisons (BLT/BLE/BGT/BGE)
    /// stay strict since callers needing tolerance should compare via BEQ on the diff.
    /// Non-numeric operands fall back to <see cref="object.Equals"/>.
    /// </summary>
    public bool Compare(string op, object? a, object? b)
    {
        // Integer paths — exact comparison (no tolerance).
        if (a is int ai && b is int bi)
        {
            return op switch
            {
                "BEQ" => ai == bi,
                "BNE" => ai != bi,
                "BLT" => ai < bi,
                "BLE" => ai <= bi,
                "BGT" => ai > bi,
                "BGE" => ai >= bi,
                _ => throw new ArgumentException($"Unknown compare op: {op}", nameof(op)),
            };
        }
        if (a is long al && b is long bl)
        {
            return op switch
            {
                "BEQ" => al == bl,
                "BNE" => al != bl,
                "BLT" => al < bl,
                "BLE" => al <= bl,
                "BGT" => al > bl,
                "BGE" => al >= bl,
                _ => throw new ArgumentException($"Unknown compare op: {op}", nameof(op)),
            };
        }

        // Numeric path — tolerance applies only to equality for floating operands.
        if (a is IConvertible && b is IConvertible)
        {
            double da = Convert.ToDouble(a, System.Globalization.CultureInfo.InvariantCulture);
            double db = Convert.ToDouble(b, System.Globalization.CultureInfo.InvariantCulture);
            const double RelTol = 1e-9;
            const double AbsTol = 1e-12;
            double absDiff = Math.Abs(da - db);
            double tol = Math.Max(Math.Max(Math.Abs(da), Math.Abs(db)) * RelTol, AbsTol);
            return op switch
            {
                "BEQ" => absDiff <= tol,
                "BNE" => absDiff > tol,
                "BLT" => da < db,
                "BLE" => da <= db,
                "BGT" => da > db,
                "BGE" => da >= db,
                _ => throw new ArgumentException($"Unknown compare op: {op}", nameof(op)),
            };
        }

        // Non-numeric fallback — only equality makes sense.
        return op switch
        {
            "BEQ" => object.Equals(a, b),
            "BNE" => !object.Equals(a, b),
            _ => throw new InvalidOperationException($"Compare op {op} requires IConvertible operands"),
        };
    }

    /// <summary>Add dispatcher: adds two integers.</summary>
    public int Add(int a, int b) => a + b;

    /// <summary>Sub dispatcher: subtracts two integers.</summary>
    public int Sub(int a, int b) => a - b;

    /// <summary>Mul dispatcher: multiplies two integers.</summary>
    public int Mul(int a, int b) => a * b;

    /// <summary>Div dispatcher: integer division of two integers.</summary>
    public int Div(int a, int b) => a / b;

    /// <summary>Mod dispatcher: modulo of two integers.</summary>
    public int Mod(int a, int b) => a % b;

    /// <summary>StringConcat dispatcher: concatenates N string arguments.</summary>
    public string StringConcat(params object?[] args)
        => string.Concat(args.Select(a => a?.ToString() ?? string.Empty));

    /// <summary>
    /// Range producer: returns the integers in <c>[from, to)</c> stepping by <c>step</c>.
    /// §十二-F: returns a strongly-typed <c>int[]</c>, not a JsonElement, so forEach
    /// binds a real int element (zero boxing).
    /// </summary>
    public int[] Range(int from, int to, int step)
    {
        if (step == 0) throw new ArgumentException("Range step must not be zero", nameof(step));
        var list = new List<int>();
        if (step > 0)
            for (int i = from; i < to; i += step) list.Add(i);
        else
            for (int i = from; i > to; i += step) list.Add(i);
        return list.ToArray();
    }
}
