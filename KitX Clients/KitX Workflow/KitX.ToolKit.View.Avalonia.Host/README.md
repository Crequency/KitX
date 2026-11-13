# KitX.ToolKit.View.Avalonia.Host

A window that talks to a running ToolKit over the View protocol. It exists so `IViewChannel` has a
consumer that is not a test.

```
dotnet run --project "KitX Clients/KitX Workflow/KitX.ToolKit.View.Avalonia.Host"
dotnet run --project ... -- --self-check          # headless, no window, exits 0 or 1
```

## What it shows

One ToolKit's surface, rendered by the same `SurfaceCard` the product uses, with values arriving over
the same `IViewChannel` a remote client would use. Nothing here is a mock: the channel is built by
`AddKitXToolKit`, the flows are compiled and executed by the real `WorkflowRunner`, and the deltas come
back through a real `DataStore` subscription.

On start it creates a sample ToolKit in `AppContext.BaseDirectory/sample-data/Toolkits` and mounts it.
That path is deliberately not the product's `./Data/Toolkits`: a demo that writes into the product's
storage root is a demo that can damage a user's installation.

## Three things this README exists to say

### 1. You will not see your Dashboard's ToolKits, and that is not a bug

This process builds its own container against its own storage root. Dashboard's mounts, values and
surfaces live in a different process with a different root. There is no cross-process transport yet —
that is P4, and it is not started.

So "the list is empty even though Dashboard has three ToolKits" is the expected answer, not a symptom.
The list is empty because *this* process has mounted nothing.

### 2. Mount first, then list — the reverse order is silent

`ListToolkits` reports only mounted ToolKits (the ruling of 2026-11-11: a View sees only what is
running). `mount` is **not** a wire command — it was removed, because a View that can mount would be a
View that changes the server's lifecycle.

The consequence is that the natural order is gone:

```
list  →  pick one  →  mount        // does not work: the list is empty, and no error says why
Mount(id)  →  list  →  connect     // the only order
```

The id has to come from somewhere other than the list — here, from the known sample. An empty list with
no error is the worst possible shape for this, so it is called out rather than left to be discovered.
`HostSessionTests.List_is_empty_until_the_sample_is_mounted` pins the ordering.

### 3. What the boundary assertion actually proves

`KitX.ToolKit.Test.Xunit/ProductBoundaryTests.cs` reads the host assembly's
`GetReferencedAssemblies()` and asserts `KitX.ToolKit` is absent. It does **not** scan the `.csproj`,
because that check passes while the real boundary is broken: `KitX.ToolKit.Hosting` references
`KitX.ToolKit` without `PrivateAssets`, so the implementation assembly is in the host's compile
references whether or not the csproj names it. (Verified: `new KitX.ToolKit.Builtin.ToolKitExecutionGlobals()`
compiles in a project that only references Hosting.)

The metadata assertion works because the C# compiler only records an assembly when the emitted IL
actually uses a type from it. So it proves, precisely:

> **The host does not name a type from `KitX.ToolKit`.** Therefore it cannot compute a value the server
> did not state — the failure mode ADR-0014 D5 exists to prevent.

Its scope stops there. A host could still reach the implementation through reflection or
`GetRequiredService<T>()` returning `object`, and that would not appear in the metadata. This is a
boundary against the *easy* mistake (typing `DataStore` because it was one using-directive away), not a
sandbox. `The_reference_table_is_not_silently_empty` guards the other direction — an empty reference
table would make the main assertion pass for no reason.

## Layout

| File | What it holds |
|---|---|
| `Program.cs` | `Application` subclass, theme wiring, `--self-check` entry |
| `HostSession.cs` | The container, the channel subscription, the projection, the card |
| `HostWindow.cs` | List on the left, card on the right, status line at the bottom |
| `SampleToolkit.cs` | The sample's config, its two flows, and its four-element surface |
| `SelfCheck.cs` | The same chain with no window, for CI and for `--self-check` |

`HostSession` is the piece worth reading: it is the only place where a wire message, a projection and a
renderer meet in the same object. The projection (`ToolkitView`) and the renderer (`SurfaceCard`) are
deliberately fed the same message independently rather than one from the other's state, so that a
disagreement between them is observable instead of being constructed away.

## Tests

- `KitX.ToolKit.Test.Xunit/HostSessionTests.cs` — the end-to-end chain (4 tests).
- `KitX.ToolKit.Test.Xunit/ProductBoundaryTests.cs` — the boundary (2 tests).

The renderer half of the same seam is pinned in `KitX.ToolKit.View.Avalonia.Test.Xunit`, which needs an
Avalonia application to construct `SurfaceCard` and therefore cannot live here.

## Known noise

`NU1903` on `Tmds.DBus.Protocol 0.21.2` (GHSA-xrw6-gwf8-vvr9) is transitive from `Avalonia.Desktop`
11.3.13, not introduced here.
