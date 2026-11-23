# KitX.ToolKit.View.Avalonia.Host

A window that talks to a running ToolKit over the View protocol. It exists so `IViewChannel` has a
consumer that is not a test.

```
dotnet run --project "KitX Clients/KitX Workflow/KitX.ToolKit.View.Avalonia.Host"
dotnet run --project ... -- --self-check          # headless, no window, exits 0 or 1
dotnet run --project ... -- --theme dark          # the other half of the theme
dotnet run --project ... -- --remote-window 5099  # the window, drawn from another process's server
```

## What it shows

One ToolKit's surface, rendered by the same `SurfaceCard` the product uses, with values arriving over
the same `IViewChannel` a remote client would use. Nothing here is a mock: the channel is built by
`AddKitXToolKit`, the flows are compiled and executed by the real `WorkflowRunner`, and the deltas come
back through a real `DataStore` subscription.

In-process mode creates a sample ToolKit in `AppContext.BaseDirectory/sample-data/Toolkits` and mounts
it on start. That path is deliberately not the product's `./Data/Toolkits`: a demo that writes into the
product's storage root is a demo that can damage a user's installation.

**Remote mode does not seed.** `--remote-window` points the window at a server that already created,
mounted and seeded whatever it is going to serve; a client that seeded would be writing a card it does
not own. That asymmetry is the whole reason `SampleSeed` is a separate object passed as a parameter
(`HostWindow(session, prepare)`) rather than a method on the session: the in-process host passes it, the
remote host passes `null`, and the difference is visible at the call site instead of inside a branch.

## Three things this README exists to say

### 1. By default you will not see your Dashboard's ToolKits — and that is still not a bug

Run with no arguments and this process builds its own container against its own storage root.
Dashboard's mounts, values and surfaces live in a different process with a different root, and nothing
crosses between them. "The list is empty even though Dashboard has three ToolKits" is the expected
answer, not a symptom: the list is empty because *this* process has mounted nothing.

`--remote-window <port>` changes which process you are looking at, not what a View is allowed to see.
It connects to a Dashboard whose View server is listening on that port, and from then on the window
draws that server's card. Two consequences worth stating plainly, because both look like bugs:

- **The list is whatever the server has mounted, and nothing else.** Dashboard does not mount anything
  on startup — a person mounts a ToolKit from its card. So a freshly launched Dashboard serves
  `list 0 row(s)` and the window says there is nothing to show. That is the same fact as the paragraph
  above, arriving through a different door: a View sees only what is running, and `mount` is not a wire
  command (see §2).
- **A View cannot make the server run anything it has not been wired for.** A button whose event has no
  trigger behind it is accepted and starts zero flows. The window says `已发送 <event>` and nothing more,
  which is honest but thin: the acknowledgement carries the count of flows that woke (`started`), and
  the window does not yet read it. `--remote <port>` does read it, and prints
  `accepted, 0 flow(s) started`.
- **A port with nobody behind it opens a window, not an exit.** The connection is attempted before the
  window exists, and a failure is not an error dialog or a silent return: `RemoteConnect` hands back a
  channel that refuses every request with the reason, so the window opens and its status line names the
  address. That keeps three different situations visibly different — *no port configured*, *connected to
  a server with nothing mounted*, *nothing listening there* — where the first two say `list 0 row(s)` and
  the third quotes the address it failed to reach.

The transport is a plain WebSocket carrying the same `ViewMessage` objects, one `ViewServer` per
`IViewChannel` connection. It is **not** the first one in this repository — Core and the plugin loaders
have been talking over Fleck on `ws://127.0.0.1:{port}` for far longer (`PluginsServer.cs:151`,
`KitX.Loader.CSharp/CommunicationManager.cs:30`). What is worth knowing is where the two agree and
where they deliberately do not:

| | plugin chain | View chain |
|---|---|---|
| library and shape | Fleck, `ws://127.0.0.1:{port}` | same |
| request correlation | `RequestId` on an out-of-band envelope | `id` on the frame itself |
| timeout | 30 s per call, in the caller | 30 s per answer, in `SocketChannel` |
| port when configured as 0 | **pick one** — 7777, then +1..+9, then a system-assigned port | **do not start** |

That last row is the one to trip over. Both fields are `int?` on `AppConfig.Web`, both default to
`null`, and `0` means opposite things: the plugin server treats it as "choose for me", the View server
treats it as "stay off". The View server's reading is the deliberate one — a listening port a person
did not ask for is a port they cannot find and did not consent to — and `ViewServerHost` logs the
refusal so the choice is visible rather than inferred from a closed socket.

No authentication, no heartbeat and no reconnection: that is scope, not oversight. It binds to
`127.0.0.1` and trusts whatever arrives.

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
| `Program.cs` | `Application` subclass, theme wiring, `--self-check` and `--remote-window` entries |
| `IViewSession.cs` | The six members a window needs, shared by the in-process and remote sessions |
| `HostSession.cs` | The container, the channel subscription, the projection, the card |
| `RemoteSession.cs` | The same six members over an `IViewChannel` somebody else's process answered on |
| `RemoteConnect.cs` | Opening the socket before the window exists, with a five-second patience |
| `UnreachableChannel.cs` | What a failed connection becomes: a channel that refuses every request |
| `SampleSeed.cs` | The three seeding steps, as one method the in-process path passes as a parameter |
| `HostWindow.cs` | List on the left, card on the right, status line at the bottom |
| `SampleToolkit.cs` | The sample's config, its two flows, and its eight-element surface over seven rows |
| `SelfCheck.cs` | The same chain with no window, for CI and for `--self-check` |
| `RemoteCheck.cs` | The same chain with no window, over a socket, for `--remote <port>` |

`HostSession` is the piece worth reading: it is the only place where a wire message, a projection and a
renderer meet in the same object. The projection (`ToolkitView`) and the renderer (`SurfaceCard`) are
deliberately fed the same message independently rather than one from the other's state, so that a
disagreement between them is observable instead of being constructed away.

### 4. `--self-check` being green does not mean the window works

This is the most expensive lesson in this directory, so it is written down rather than remembered.

`--self-check` returns from `Main` **before** any Avalonia lifetime is built. That means it runs on a
plain thread-pool thread with **no `SynchronizationContext`**. The window runs on the Avalonia UI
thread, which installs one. Awaiting a file write behaves differently in the two:

```
no context   : continuation runs on the pool      → blocking is merely slow
UI thread    : continuation is posted back to the  → blocking that thread
               thread that is blocking              deadlocks
```

The first launch of this window did exactly that. `SampleToolkit` wrote the sample's `.kcs` bodies with
`SaveAsync(...).GetAwaiter().GetResult()`; the write's continuation was posted to the UI thread; the UI
thread was inside `GetResult()` waiting for the write. Nothing threw, nothing was logged, the window
never drew, and the disk held `scan.kcs.tmp` with no `scan.kcs`.

Every test in this repository runs without a `SynchronizationContext`, so no test could see it — the
same code, two hosts, one green and one dead. The regression that does see it is
`SampleCreationThreadingTests`, which builds a real pump thread and runs creation on it; with the old
blocking shape it fails after the 30-second invoke timeout.

The fix is not `ConfigureAwait(false)` at the bottom of the file stack (that makes blocking less likely
to kill you). It is that `EnsureSampleAsync` is async all the way down, and its callers await it — a
caller on the UI thread then returns to the dispatcher instead of parking it.
**`--remote-window` sits on both sides of that line, deliberately.** The socket is opened in `Main`
before any Avalonia lifetime exists, so that `await` has no `SynchronizationContext` and its continuation
comes back on the pool — the safe column of the table above. By the time the window is constructed the
channel is already a finished object, and every later await in `HostWindow` happens on the UI thread with
its context installed. That is why `Program.cs` passes an `IViewChannel` and not a port number: the one
await that could be dangerous is done before there is a thread it could deadlock.

## Tests

- `KitX.ToolKit.Test.Xunit/HostSessionTests.cs` — the end-to-end chain (4 tests).
- `KitX.ToolKit.Test.Xunit/ProductBoundaryTests.cs` — the boundary (2 tests).
- `KitX.ToolKit.Test.Xunit/SampleCreationThreadingTests.cs` — the deadlock above (1 test).
- `KitX.ToolKit.View.Avalonia.Test.Xunit/RemoteWindowTests.cs` — the remote window draws the server's
  card; it writes only the two requests a start-up is allowed to make; and a refused connection opens a
  window that says so instead of drawing a blank card that reads like an empty server (3 tests).

The renderer half of the same seam is pinned in `KitX.ToolKit.View.Avalonia.Test.Xunit`, which needs an
Avalonia application to construct `SurfaceCard` and therefore cannot live here.

### 5. Both halves of the theme, measured

`--theme dark` exists because the theme ships two resource dictionaries (`KitX.axaml:31`, `:51`) and this
window is the only place the renderer runs in a real window. Without a way to ask for the second one, half
the theme's own keys are never drawn by anything a person can look at.

Both screenshots were taken from the same binary, one run apart, and every flat colour inside the card was
matched against the value the dictionary declares:

```
                     light              dark          key
card background      #FFFFFF  218 990px #252525  218 990px  KitXCardBg
list group           #FAFAFA  232 816px #2A2A2A  232 800px  KitXGroupBg
field background     #FBFBFB   25 808px #2E2E2E   25 826px  KitXFieldBg
window background    #F5F5F5   21 794px #2B2B2B   22 259px  KitXSubtleBg
danger button        #FDF3F3   17 463px #3B2325   17 463px  KitXDangerBg
primary button       #3873D9    8 232px #4B8BEA    8 463px  KitXAccent
card border          #E5E5E5            #3A3A3A             KitXCardBorder
```

Six keys agree to the byte in both themes, which is what says the window is drawing the theme rather than
something that merely looks dark. The **pixel counts** differ in the last columns because the empty list
band is taller in dark — the same band, drawn at the same DIP, so the point is that the *colours* match,
not that the tallies do.

The 1px `KitXCardBorder` is present in both, but only on the stretches where the card's own edge is not
covered by a list group. Measured down `x=379` in the light screenshot:

```
y  78..250    border    (title / status / field / button rows)
y 287..470    border
y 489..948    no border (the list group's #FAFAFA runs to the card edge)
y 948..1116   border    (the empty tail of the card)
```

That is the reference image's own behaviour too (`Shots/out/01-scanbook-light.png`: border at y 24, group
starting at y 372 flush to the edge), so it is the card being 390 wide with a full-bleed list, not a
missing stroke.

The band geometry, converted to device-independent pixels (the window draws at 1.2436× — 485 device pixels
for a 390 DIP card — while the reference PNG is 1:1):

| band | host (DIP) | reference (DIP) | Δ start |
|---|---|---|---|
| title row | 0.0 – 143.9 | 27.0 – 109.0 | −27.0 |
| status row | 177.7 – 208.3 | 122.0 – 170.0 | +55.7 |
| field row | 242.0 – 272.6 | 203.0 – 234.0 | +39.0 |
| button row | 275.0 – 304.0 | 267.0 – 298.0 | +8.0 |
| list group | 322.5 – 706.8 | 347.0 – 730.0 | −24.5 |
| card tail | 708.4 – 849.1 | 731.0 – 871.0 | −22.6 |

**The card is 868 DIP tall in the host against 845 in the reference**, and the deltas say why: this sample
card's first row holds one Text with a title (`18`) plus a status row, while the reference's first row
holds a `13`-point title and its second holds a `13`-point author line. Same six bands in the same order,
distributed over a slightly different content height. **This is not a layout defect** and is recorded here
only because a 23 DIP difference in card height looks like one until the content is compared.


## Known noise

`NU1903` on `Tmds.DBus.Protocol 0.21.2` (GHSA-xrw6-gwf8-vvr9) is transitive from `Avalonia.Desktop`
11.3.13, not introduced here.
