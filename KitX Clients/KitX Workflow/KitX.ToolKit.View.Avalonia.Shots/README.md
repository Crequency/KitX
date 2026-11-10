# Renderer shots

The renderer's own output path. Every image under `out/` is produced by this project from a JSON
scenario, so a picture is reproducible by anyone who has the repository.

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
pictures.

## The images themselves are reproducible byte for byte

The ten PNGs are committed. Running the tool again on a clean tree leaves `git status --porcelain`
empty — not "looks the same", the same bytes. That was measured, not designed, and it means the
images work as a second baseline:

- the self-checks above catch the two failures that were already known;
- a diff in `out/` catches the ones nobody has thought of yet.

If a change is meant to alter the rendering, the diff is the review. If it is not, the diff is the
bug.

