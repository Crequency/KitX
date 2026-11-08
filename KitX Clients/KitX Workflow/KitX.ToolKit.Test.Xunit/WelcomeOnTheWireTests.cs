using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using KitX.ToolKit.Contract.Declaration;
using KitX.ToolKit.Contract.View;
using KitX.ToolKit.Data;
using KitX.ToolKit.Instances;
using KitX.ToolKit.Run;
using KitX.ToolKit.Storage;
using KitX.ToolKit.Surface;
using KitX.ToolKit.Triggers;
using KitX.ToolKit.View;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace KitX.ToolKit.Test.Xunit;

/// <summary>
/// One half of the seam: what the server actually puts on the wire for a `welcome`.
/// </summary>
/// <remarks>
/// <para><b>Why this half lives here and the other half lives elsewhere.</b> A single test could
/// build the card and feed it the message in one process — but that would only prove the two pieces
/// can run together. Splitting them at the JSON proves the harder thing: that the message between
/// them is <em>enough</em>. Objects passed in memory never serialise, so a missing field there would
/// go unnoticed.</para>
///
/// <para><b>The JSON below is not hand-written.</b> It is what <see cref="ViewSession.Attach"/>
/// produced on 2026-11-08. If the contract's shape changes, this test fails and the other half has
/// to be updated with it — which is the point: the two halves are pinned to the same bytes.</para>
/// </remarks>
public sealed class WelcomeOnTheWireTests
{
    private const string TkId = "tk-seam";

    /// <summary>The exact bytes the other half deserialises. Keep them in step.</summary>
    /// <remarks>
    /// <para>Three things here were not what I expected, and the assertion caught each one — I had
    /// already "fixed" this constant twice by guessing:</para>
    ///
    /// <list type="number">
    /// <item><b>The element discriminator is <c>"Text"</c>, capitalised.</b> That is what
    /// <c>[JsonDerivedType]</c> declares. A lowercase "text" is silently an unknown type, which the
    /// store drops with a warning — the card came back empty and nothing said why.</item>
    /// <item><b><c>protocolVersion</c> is a number</b>, not a string.</item>
    /// <item><b>Nulls are written out.</b> Every element carries <c>"format":null</c> and
    /// <c>"name":null</c>. That is the serializer's default, and it means the wire is larger than
    /// the data and a consumer cannot tell "absent" from "explicitly null".</item>
    /// </list>
    ///
    /// <para>This constant is the seam. If it changes, both halves move together — which is the
    /// property that makes splitting this test across two assemblies worth the trouble.</para>
    /// </remarks>
    internal const string Wire =
        """{"type":"welcome","toolkitId":"tk-seam","values":{"title":"Clean Code"},"surface":{"schemaVersion":1,"columns":4,"elements":[{"type":"Text","format":null,"row":1,"column":1,"columnSpan":1,"key":"title","name":null},{"type":"Text","format":null,"row":2,"column":1,"columnSpan":1,"key":"cover","name":null}]},"protocolVersion":1}""";

    private readonly ITestOutputHelper _output;

    public WelcomeOnTheWireTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Attach_produces_a_message_that_serialises_to_the_shape_the_other_half_reads()
    {
        using var root = new TempToolkitRoot();

        var toolkit = new Toolkit
        {
            Id = TkId,
            Keys =
            [
                new KeyDeclaration("title", "string", KeyRole.Out),
                new KeyDeclaration("cover", "string", KeyRole.Out),
            ],
        };

        // Prepare returns the root it wrote under (line 92: `return root`), not the toolKit's own
        // directory — the toolKit folder is made inside it.
        var prepared = root.Prepare(toolkit);

        // SurfaceStore resolves <root>/<toolkitId>/surface.json.
        File.WriteAllText(
            Path.Combine(prepared, TkId, SurfaceStore.FileName),
            """
            {
              "schemaVersion": 1,
              "columns": 4,
              "elements": [
                { "type": "Text", "row": 1, "column": 1, "key": "title" },
                { "type": "Text", "row": 2, "column": 1, "key": "cover" }
              ]
            }
            """);

        var store = new DataStore();
        var manager = new ToolkitInstanceManager(
            new ServiceCollection().BuildServiceProvider(),
            TriggerSourceRegistry.BuildDefault(),
            new SilentExecutor(),
            store,
            _ => new ToolkitFileStore(prepared));

        using (manager)
        {
            manager.Mount(toolkit);
            store.Set($"{TkId}/title", "Clean Code");

            var session = new ViewSession("s1", manager, store, new SurfaceStore(root.Root));
            using (session)
            {
                // Serialised through the base type on purpose: that is what the channel does, and
                // it is the only way the `type` discriminator appears. Serialising a WelcomeMessage
                // variable directly would omit it — the polymorphic annotation is a property of the
                // base type, and a concrete static type never reaches it.
                ViewMessage welcome = session.Attach(TkId);

                // Load once more, directly: if the card is empty, the reason is a load error or a
                // dropped element, and both are silent from the card's side.
                var probe = new SurfaceStore(root.Root).Load(TkId);
                foreach (var e in probe.Errors)
                    _output.WriteLine($"load error {e.Code}: {e.Message}");
                foreach (var w in probe.Warnings)
                    _output.WriteLine($"load warning {w.Code}: {w.Message}");
                _output.WriteLine($"elements loaded: {probe.Document.Elements.Count}");
                _output.WriteLine($"surface path: {Path.Combine(prepared, SurfaceStore.FileName)}");

                var json = JsonSerializer.Serialize(
                    welcome, new JsonSerializerOptions(JsonSerializerDefaults.Web));

                _output.WriteLine(json);
                Assert.Equal(Wire, json);
            }
        }
    }

    private sealed class SilentExecutor : IWorkflowExecutor
    {
        public Task<WorkflowExecutionResult> ExecuteAsync(
            string workflowId, string filePath,
            IReadOnlyDictionary<string, string?>? overrides, CancellationToken ct)
            => Task.FromResult(new WorkflowExecutionResult(workflowId, true, null, null));
    }
}
