using System.Text.Json;
using KitX.Core.Contract.Workflow;
using KitX.ToolKit.Contract.Declaration;
using KitX.WorkflowV6.Lens.KsTextLens;
using KitX.WorkflowV6.Serialization;
using Xunit;

namespace KitX.ToolKit.Test.Xunit;

/// <summary>
/// A throwaway toolkit storage root that actually contains the files the config declares.
///
/// <para>
/// WHY THIS EXISTS: <c>Mount</c> compiles the config, and compiling inspects each declared
/// <c>.kcs</c> — it checks the file exists, that it is a real <c>.kcs</c> document, and that the
/// builtin names inside it are ones the host provides. Before those checks were wired to the
/// production path, a test could declare <c>File = "wf.kcs"</c> against a temp directory that held
/// nothing and still mount — which is how ~38 tests came to depend on a file-lookup bug. The tests
/// were not wrong about what they wanted to exercise (lifecycle, key roles, instance bookkeeping);
/// they just never had to make the declaration true.
/// </para>
/// <para>
/// The bodies are parsed from KS rather than hand-built, so the IR is the same shape the editor
/// produces. Tests that substitute the executor never run them; the content only has to be a
/// document the compiler will read.
/// </para>
/// </summary>
internal sealed class TempToolkitRoot : IDisposable
{
    public TempToolkitRoot()
    {
        Root = Path.Combine(Path.GetTempPath(), "kitx-tk-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    /// <summary>
    /// Writes a valid <c>.kcs</c> for every workflow the toolkit declares and returns
    /// <see cref="Root"/>, shaped so it can be dropped straight into a file-store factory.
    /// </summary>
    /// <param name="ks">
    /// Optional body, in KS, keyed by workflow id. Workflows without an entry get a body that
    /// declares nothing — enough for a compile, because these tests assert about bookkeeping
    /// rather than about what a workflow computes.
    /// </param>
    public string Prepare(Toolkit toolkit, IReadOnlyDictionary<string, string>? ks = null)
        => Write(Root, toolkit, ks);

    /// <summary>
    /// Writes the declared workflow files under <paramref name="root"/>. Static so a test that
    /// owns its own root (the composition-root tests, whose container is bound to it) can use the
    /// same writer instead of duplicating the <c>.kcs</c> shape.
    /// </summary>
    public static string Write(string root, Toolkit toolkit, IReadOnlyDictionary<string, string>? ks = null)
    {
        var dir = Path.Combine(root, toolkit.GetId());
        Directory.CreateDirectory(dir);

        // An empty registry is sufficient: parsing does not reject unknown call targets, and
        // the bodies here call nothing.
        var lens = new KsTextLens(new KitX.WorkflowV6.Builtin.BuiltinFunctionRegistry());

        foreach (var wf in toolkit.Workflows)
        {
            if (string.IsNullOrWhiteSpace(wf.File))
                continue;

            // An empty KS body serialises to "{}", which the compiler rejects as "not a .kcs
            // document" — the check exists to catch truncated files. This body parses to a real
            // pipeline and calls only FlowIn/FlowOut, which every host catalogue provides.
            var body = ks is not null && ks.TryGetValue(wf.Id, out var given)
                ? given
                : "FlowIn(\"in\", \"\") > FlowOut(\"out\", _)";
            var ir = lens.Parse(body + "\n", []);

            var kcs = new KcsFileFormat
            {
                Id = wf.Id,
                Name = wf.Name,
                IrData = WorkflowSerializer.Serialize(ir),
            };

            var path = Path.Combine(dir, wf.File);
            var parent = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(parent))
                Directory.CreateDirectory(parent);

            File.WriteAllText(path, JsonSerializer.Serialize(kcs));
        }

        return root;
    }

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); }
        catch { /* best-effort temp cleanup */ }
    }
}
