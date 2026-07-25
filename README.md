# Ear Tags

A Vintage Story mod that lets you clip small dyed-leather ear tags onto your livestock, so you can
tell individual animals apart at a glance — which generation is which, who's your best breeder, and
which sheep is the one you actually meant to shear.

Ten colours, two ears per animal, so a hundred distinguishable combinations.

Built for Vintage Story 1.22.

## Using it

Craft a tag, hold it, and right-click an animal to clip it to a bare ear.

- **Right-click** — clip to the first free ear
- **Sneak + right-click** — clip specifically to the right ear
- **Sneak + right-click with an empty hand** — remove a tag and get it back

Hovering a tagged animal shows what it's wearing: `Ear tags: left red, right blue`.

## Crafting

Shapeless, in any three slots:

```
knife  +  dyed leather  +  beeswax | rendered fat | 0.25 L oil   →   8 tags
```

The tag takes the colour of the leather, so it uses vanilla's existing barrel dyeing — no new dye
mechanics. Oil can be drawn from a bowl, bucket or jug, and accepts vanilla flax (linseed) and
olive oil plus all seven Expanded Foods oils if that mod is installed.

Leather is the right material here: pre-plastic livestock tags really were leather or punched metal,
and the wax/fat/oil rub is genuine leather treatment rather than a hand-wave.

## Supported animals

| Animal | Status |
|---|---|
| Sheep — mouflon | Tuned |
| Sheep — bighorn | Tuned |
| Pigs — eurasian, red river, warthog | Tuned |
| Goats — 11 breeds | Not yet, see below |

## Adding a species

Placement lives entirely in `assets/eartags/config/attachpoints.json` and is hot-editable — change
the numbers, reload the world, no recompile. Each species needs its ear bone names and an offset.

Coordinates are **local to the ear bone**, with `[0,0,0]` at the bone's own `from` corner. Every
ear bone in this family carries a mirrored `rotationX`, which swings the local axes:

- **Y** — along the ear, `0` at the base by the skull. Positive moves toward the tip. **Same sign
  on both ears.**
- **Z** — vertical once rotated. Positive hangs the tag down on the left ear but *up* on the right.
  **Mirrored sign.**
- **X** — ear thickness. Only needed when an ear is thicker than the mouflon's `0.4`.

Adding a new species also needs a JSON patch giving it the `eartaggable` behaviour and the ten tag
textures — see `assets/eartags/patches/`.

### Goats, when someone gets to them

Groundwork is done, the tuning isn't:

- Bone names are lowercase `L ear` / `R ear`, unlike the sheep's `L Ear`.
- Eleven breeds with very different ears, from `0.4 x 1.7 x 0.8` (mountain) to `1.6 x 5.0 x 0.3`
  (sirohi). Each wants its own entry.
- **`angora`, `nubian` and `sirohi` are thin on Z rather than X**, so the tag mounts perpendicular
  to the ear. They need a 90° `rotation` entry.
- `goat-adult.json` already declares `texturesByType` for `angora` and `mountain`, which may
  override an added `textures` block for those two — untested.

## Live tuning commands

- `.eartags show` — current offsets for the species you're looking at
- `.eartags nudge x|y|z amount` — shift the tag; `z` is auto-mirrored between ears
- `.eartags scale n` — resize
- `.eartags save` — write values back to `attachpoints.json`, preserving comments
- `.eartags reload` — re-read the file from disk
- `.eartagfreeze` — pin the animal you're looking at and stop its animations while you tune

Reload and save read/write the config through `Mod.SourcePath`, so they only work while the mod is
an unpacked folder. Zip it and you're back to hand-editing plus a world reload.

## How the rendering works

The tag is step-parented onto the animal's ear bone at tesselation time — the same mechanism vanilla
uses for the mouflon's mane and the boar's tusks — so it animates with the ear for free. Tag colours
are stored per-ear in `WatchedAttributes`, so they sync to clients and survive save/load.

## Notes for anyone hacking on this

This is a **source mod**: the game compiles it with Roslyn at startup, with a restricted reference
set. Some things that cost real time to discover:

- **`Dictionary<,>` and `List<T>` are unusable** — the compiler doesn't reference the
  `System.Collections` facade, so any use is `CS0012`. Arrays and `StringBuilder` only. This also
  bans *touching* API members typed as those collections. A single compile error takes down the
  whole mod, and the log still prints "Successfully compiled N source files" right afterwards.
- **Entity textures must be declared at asset-load time**, via a JSON patch to the entity's
  `/client/textures`. Registering them at runtime produces an entry that looks correct in every
  observable way and still renders blank.
- **No `//` comments inside a recipe JSON array** — the grid-recipe loader treats each array token
  as a recipe, so every comment line becomes a "Failed to parse grid recipe" error.
- **In a mod's recipe file, an unprefixed code resolves to the mod's domain**, not `game`. Vanilla
  recipes get away with bare `leather-normal-*` only because they live in `game`. Unresolvable
  ingredients are dropped *silently* — watch the "N grid recipes loaded" count, not the error log.
- **Chat output is parsed as VTML**, so `<angle brackets>` in a message silently swallow the rest
  of the line.
- `VintagestoryAPI.xml` ships with the game and is the best way to verify API signatures.
