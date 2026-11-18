# Renderer shots

The renderer's own output path. Every image under `out/` is produced by this project from a JSON
scenario, so anyone with the repository can regenerate a picture with one command.

```sh
dotnet run --project "KitX Clients/KitX Workflow/KitX.ToolKit.View.Avalonia.Shots"
```

Images land in `out/<name>-light.png` and `out/<name>-dark.png` — relative to **this project**, not to
`bin/`. The tool refuses to write anywhere under a `bin/` or `obj/` directory: images there vanish on
`dotnet clean` and nobody looks at them, which defeats the point of the whole thing.

## Adding a scenario

Add a `.json` file to `scenarios/`. Nothing else. The file name stem is used for the images.

```json
{
  "name": "06-example",
  "caption": "What a person is supposed to see here",
  "document": { "schemaVersion": 1, "layoutVersion": 1, "elements": [ ... ] },
  "values": { "title": "\"a string value\"", "rows": "[\"an\", \"array\"]" },
  "click": "结束这一批"
}
```

- `document` is a `SurfaceDocument`, the same shape the renderer and the orchestrator exchange.
- `values` are **JSON text**, not strings: a `Text` displays a string and a `List` displays an array.
  Only those two kinds display a server-supplied value; the four editing controls deliberately do
  not, and naming one here is asking the renderer to do something it refuses.
- `click` opens an overlay by clicking a button after layout, the same way a person would. A second
  document that merely looks like a confirmation would not prove the confirmation exists.

## Why this is not a demo, and not a test

It is **not a demo** because a picture produced by an untracked program is reproducible on exactly
one machine. Adding a case here means adding data, not editing C#.

It is **not a test** because correctness is decided elsewhere — `KitX.ToolKit.View.Avalonia.Test.Xunit`
asserts geometry against `SurfaceLayout`'s arithmetic, and that is where a layout rule should be
pinned. This tool exists for the questions arithmetic cannot answer: does the theme reach the pixels,
is the dark variant readable, does the confirmation actually dim the card.

## The self-checks

Writing files is not enough — a tool that only writes files will happily write a picture of the wrong
thing forever. Each run therefore also asserts, per image:

1. every declared value reached a `Text` or `List` on the card (a value that silently vanishes is
   invisible in a PNG that was already blank for another reason);
2. for a scenario with `click`, the scrim exists, has a non-transparent brush, and covers the whole
   card within half a pixel.

Those two are not hypothetical. Both were real defects found on 2026-11-10 by looking at these
images, and **every unit test had let them through**:

- the confirmation overlay was pinned to row 0, so the dialog was clipped and its buttons were
  unreachable — `DangerConfirmTests` asked only whether the overlay existed and whether its buttons
  could be clicked, and neither question involves geometry;
- the overlay had no background at all — a `TryFindResource` resolved to `null` because it ran before
  the card had a visual root, and a local `null` outranks the theme's setter.

A run that fails a check exits non-zero, so this can be wired into CI without anyone watching the
pictures. Whether anything actually runs it is a different question, and it is answered in
[`Keeping out/` honest](#keeping-out-honest): nothing does.

## Running twice on one machine gives the same bytes

The ten PNGs are committed. Running the tool again on a clean tree leaves `git status --porcelain`
empty — not "looks the same", the same bytes.

**The scope of that claim is narrow, and it matters.** It was measured on one machine, one `bin/`,
one day. This repository pins neither the NuGet graph (`packages.lock.json`: none) nor the SDK
(`global.json`: none), so a different clone may resolve different packages and a different SDK.

**State it behaviourally, not causally.** What was observed is: *given the committed `out/`, a rerun
produced no difference.* That sentence does not name a cause. Whether the cause is "no randomness",
"a fixed seed", or "the same value happened to come out twice" is **not** established — a coincidental
repeat would look exactly like this. So the honest claim is the observation, not the explanation.

It does **not** follow that two clones produce identical files.

That is still enough for the images to work as a second baseline:

- the self-checks above catch the two failures that were already known;
- a diff in `out/` catches the ones nobody has thought of yet.

If a change is meant to alter the rendering, the diff is the review. If it is not, the diff is the
bug.
## Keeping `out/` honest

The images are committed, so they can go stale. **Nothing automated will notice.** None of the nine
workflows under `.github/workflows/` mentions this project — `grep 'Avalonia.Shots'` returns nothing
— and that is not an oversight waiting to be fixed, it is the state this tool has always been in. So
this section is not documentation of a mechanism; **it is the only mechanism.**

### Write the prediction down before you run it

> **Before rendering, write one line: which images should change. After rendering, compare against
> that line.**

A prediction made after looking at the diff is not a prediction. Both of these are true statements
about the same run, and only the second one checked anything:

- "the two confirmation shots changed, which is what I expected" — said after seeing the diff;
- "`03-confirm-light`, `03-confirm-dark`" — written before the run, and therefore falsifiable.

Without the written line, every diff can be read as expected, because `expected` is a past tense.
The failure this prevents is not a wrong picture; it is a **review that could not have failed**.

### What that looks like, from a real run

The dialog's width and padding changed on 2026-11-18. The prediction was two images. The run gave:

```
01-scanbook-light        09503F1105DE6D00  unchanged
01-scanbook-dark         A62E1D863DB94245  unchanged
02-danger-light          900AF3F69551182B  unchanged
02-danger-dark           0C38EFC4D7B15EF2  unchanged
03-confirm-light         56C748761FCB9CBC -> 5B3DA42B85801960   changed
03-confirm-dark          B18787A8D5A46923 -> 37541FBBB029AA7C   changed
04-weights-light         E0BEFD9AF133E028  unchanged
04-weights-dark          0288C0ED8119E13A  unchanged
05-empty-light           FBC1CBE7AF8C2422  unchanged
05-empty-dark            BFF0CBFE342960ED  unchanged
```

Eight untouched is the part that took work. "The picture I meant to change changed" is one question;
"and nothing else did" is a different one, and only the second can catch a change that reached the
renderer twice.

The tool prints those hashes on every run (`Save()`, one line per image), so building that table costs
one command rather than a script. The first 16 characters are enough for the comparison this section
asks for — the full 64 are one `Get-FileHash` away on either side, and a committed list of them would
be a second copy of the truth that drifts the first time the images are regenerated without it.

### Then classify what you got, in this order

> **After changing the renderer or the theme, run this tool again — then ask three questions, in this order.**

1. **Which ones changed?** Compare against the line you wrote down. Fewer means your change never
   reached the render. More means you touched something else.
2. **Which ones did not?** For each, ask: *can the quantity I changed affect this card at all, by
   design?* Yes → you are wrong. No → correct.
3. A card whose whole size is fixed (like `05-empty`, two rows) belongs to case 2 whenever the change
   is a per-slot dimension. That is a correct "no change", not a miss.

**Why this is not the one-liner it replaced.** "No change" has three causes: the change genuinely
does not affect that card, the change was cancelled by something else, or the change never reached
the renderer at all. **One observation, three causes.** The old rule read all three as "you are
wrong" — and `SlotHeight` 64→65 on `05-empty` is a measured counter-example of cause 1.

> This rule cannot answer "did I get it wrong?" for you. **It can only make you ask that question
> at the moment you look at the diff.** The classification needs a person, and that is the point.

