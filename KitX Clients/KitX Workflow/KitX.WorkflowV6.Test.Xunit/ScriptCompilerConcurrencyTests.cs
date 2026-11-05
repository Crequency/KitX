// ─────────────────────────────────────────────────────────────────────────────
// ScriptCompiler cache concurrency.
//
// The cache is a plain Dictionary plus a LinkedList, and `StructuredRoslynBackend`
// — which builds its own compiler — is registered with AddSingleton. So the same
// two collections are driven by every workflow execution at once.
//
// Neither collection fails loudly when it is hit concurrently: a Dictionary can
// corrupt its buckets or spin, a LinkedList can lose nodes. Both would show up as
// flakiness under load and never in a single-threaded run, which is why the gap
// survived this long.
//
// These tests do not measure speed. They drive the cache from many threads at
// once and assert that every caller still gets a working assembly — the cheapest
// thing a correct lock guarantees and the first thing its absence breaks.
// ─────────────────────────────────────────────────────────────────────────────

using KitX.WorkflowV6.Backend.RoslynBackend;
using KitX.WorkflowV6.Builtin;
using KitX.WorkflowV6.Backend.Runtime;
using KitX.WorkflowV6.Ir;
using KitX.WorkflowV6.Lens.KsTextLens;
using Xunit;

namespace KitX.WorkflowV6.Test.Xunit;

public class ScriptCompilerConcurrencyTests
{
    /// <summary>One compiler shared by every thread, the way the container does it.</summary>
    private static (ScriptCompiler Compiler, KsTextLens Lens) MakeCompiler()
    {
        var registry = BuiltinFunctionRegistry.Discover(typeof(BuiltinFunctionRegistry).Assembly);
        return (new ScriptCompiler(registry, typeof(ExecutionGlobals), maxCacheEntries: 4), new KsTextLens(registry));
    }

    private static Workflow Parse(KsTextLens lens, string source) => lens.Parse(source, []);

    [Fact]
    public async Task Compiling_the_same_script_from_many_threads_always_yields_a_usable_assembly()
    {
        var (compiler, lens) = MakeCompiler();
        var ir = Parse(lens, "Print\n");

        const int callers = 32;
        var failures = new System.Collections.Concurrent.ConcurrentQueue<string>();

        await Task.WhenAll(Enumerable.Range(0, callers).Select(_ => Task.Run(() =>
        {
            var (assembly, _, errors) = compiler.Compile(ir, null, isDebug: false);
            if (assembly is null)
                failures.Enqueue(string.Join("; ", errors));
        })));

        Assert.Empty(failures);
    }

    [Fact]
    public async Task Distinct_scripts_racing_for_a_tiny_cache_all_succeed()
    {
        // maxCacheEntries is 4 and this asks for 16 distinct hashes. Every caller therefore
        // both stores and evicts while the others are storing and evicting — the interleaving
        // that a missing lock corrupts first.
        var (compiler, lens) = MakeCompiler();
        var scripts = Enumerable.Range(0, 16)
            .Select(i => Parse(lens, $"Print\nPrint\nPrint({i})\n"))
            .ToList();

        var failures = new System.Collections.Concurrent.ConcurrentQueue<string>();
        await Task.WhenAll(scripts.Select(ir => Task.Run(() =>
        {
            var (assembly, _, errors) = compiler.Compile(ir, null, isDebug: false);
            if (assembly is null)
                failures.Enqueue(string.Join("; ", errors));
        })));

        Assert.Empty(failures);
    }

    [Fact]
    public async Task Clearing_the_cache_while_others_compile_does_not_throw()
    {
        var (compiler, lens) = MakeCompiler();
        var scripts = Enumerable.Range(0, 8)
            .Select(i => Parse(lens, $"Print({i})\n"))
            .ToList();

        using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var failures = new System.Collections.Concurrent.ConcurrentQueue<string>();

        var compiling = scripts.SelectMany(ir => Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            try
            {
                var (assembly, _, errors) = compiler.Compile(ir, null, isDebug: false);
                if (assembly is null)
                    failures.Enqueue(string.Join("; ", errors));
            }
            catch (Exception ex)
            {
                failures.Enqueue(ex.GetType().Name + ": " + ex.Message);
            }
        }))).ToList();

        // ClearCache walks the same dictionary and linked list. Concurrent enumeration of a
        // collection being mutated is exactly what `InvalidOperationException: Collection was
        // modified` is for — the lock is what makes this loop safe rather than lucky.
        var clearing = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                try { compiler.ClearCache(); }
                catch (Exception ex) { failures.Enqueue("ClearCache " + ex.GetType().Name); }
            }
        });

        await Task.WhenAll(compiling);
        await clearing;

        Assert.Empty(failures);
    }
}
