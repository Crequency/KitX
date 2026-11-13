using System;
using System.Collections.Generic;
using System.Linq;
using KitX.ToolKit.Contract;
using KitX.ToolKit.Contract.Declaration;
using KitX.ToolKit.Contract.Surface;
using KitX.ToolKit.Storage;
using KitX.ToolKit.Surface;
using Xunit;

namespace KitX.ToolKit.Test.Xunit;

/// <summary>
/// <c>ISurfaceStore.Diagnose</c> is wired to the same validator the render path uses, and it is a
/// question, not a gate.
/// </summary>
/// <remarks>
/// <para><b>This file exists because the editor's own tests cannot cover this.</b>
/// <c>SurfaceValidator</c> and <c>KeyTable</c> are internal to this assembly, so
/// <c>SurfaceEditorSaveTests</c> in the Dashboard project hands the editor supplied diagnostics and
/// asserts only that it reports them and writes first. That file would keep passing if
/// <c>Diagnose</c> were wired to a constant empty list. This one is what notices.</para>
///
/// <para><b>Deleting the wiring is the mutation this is written against</b>: make
/// <c>SurfaceStore.Diagnose</c> return <c>[]</c> and the first test below goes red.</para>
/// </remarks>
[Trait("Category", "Unit")]
public class SurfaceStoreDiagnoseTests
{
    private static readonly KeyDeclaration[] Declared =
    [
        new("title", "string", KeyRole.Out),
    ];

    [Fact]
    public void An_undeclared_key_comes_back_as_a_diagnostic_naming_the_key()
    {
        var document = new SurfaceDocument
        {
            Elements = [ new SurfaceTextElement { Row = 1, Weight = 1, Key = "ghost" } ],
        };

        var warnings = new SurfaceStore(PathOfNothing()).Diagnose(document, Declared);

        var warning = Assert.Single(warnings);
        Assert.Equal(SurfaceDiagnosticCodes.ElementUnknownKey, warning.Code);
        Assert.Equal("ghost", warning.Path?.Key);

        // The question carries no ToolKit: the caller is looking at one already. A guessed id here
        // would put a plausible-looking wrong value into a machine-readable path.
        Assert.Equal(string.Empty, warning.Path?.ToolkitId);
    }

    [Fact]
    public void A_document_that_would_survive_validation_produces_nothing()
    {
        var document = new SurfaceDocument
        {
            Elements = [ new SurfaceTextElement { Row = 1, Weight = 1, Key = "title" } ],
        };

        Assert.Empty(new SurfaceStore(PathOfNothing()).Diagnose(document, Declared));
    }

    /// <summary>A root nothing is read from or written to: every test here builds its document.</summary>
    private static string PathOfNothing() =>
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "kitx-diagnose-" + Guid.NewGuid().ToString("N"));
}