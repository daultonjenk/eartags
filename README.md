# Animal Tags

A Vintage Story mod that lets you clip small dyed-leather tags onto your livestock, so you can
tell individual animals apart at a glance — which generation is which, who's your best breeder, and
which sheep is the one you actually meant to shear.

Ten colours, two ears per animal, so a hundred distinguishable combinations.

Chickens have no ear to speak of, so the same tag goes round a leg instead, as a poultry leg band.

There is also a **metal** tag, smithed on an anvil, which does two things more. It catches the light
the way an ingot does. And nothing the player does can hurt the animal wearing it — no cleaver at
harvest time, no stray punch while mining the block behind it. Mark your prize breeder and it stays
alive until you take the tag back off. Wolves, falls and drowning are unaffected: protection, not
immortality.

Built for Vintage Story 1.22.

> The mod id and every item code still read `eartags`. They are what's already saved on your
> animals and sitting in your chests, so only the display name changed.

## Using it

Craft a tag, hold it, and right-click an animal to clip it to a bare ear.

- **Right-click** — clip to the first free ear
- **Sneak + right-click** — clip specifically to the right ear
- **Sneak + right-click with an empty hand** — remove a tag and get it back

Hovering a tagged animal shows what it's wearing as a coloured square per side, with a shield
between them if it's protected — `Animal Tags: ■ ⛊ ■`, or `Leg bands: ■ ■` on a bird. A bare side
keeps its place as a hollow `▫`, because *which* side a tag is on is half the information.

That's one line for everything this mod knows about an animal. Set `visualInfo: false` in
`ModConfig/eartags.json` for the older wording (`left red, right blue` plus a separate protection
line) — do that if the squares come out as literal `<font>` markup, which would mean the info panel
doesn't parse VTML on your version.

The glyphs live in `assets/eartags/lang/en.json` as `info-swatch`, `info-swatch-bare` and
`info-protected`. If your font renders any of them as an empty box, swapping it is a lang edit
rather than a code change.

The entity's **name** is deliberately untouched. `EntityBehavior` has no `GetName` hook, so
decorating the title would mean either replacing the entity `class` — which collides with any other
mod wanting that slot — or Harmony-patching a method every mod reads. Neither is worth one line of
polish, so the mod says everything it has to say on its own line.

Swatch colours are a `switch` in `EarTagsModSystem.MaterialHex` — thirty-three fixed values, one
per dye and per metal.

Applying and removing a tag is **silent** by default — the leather sound is the confirmation.
Tagging a whole flock would otherwise bury the chat. `.eartagmessages` turns the chat lines on and
off and remembers the choice in `ModConfig/eartags.json`. Refusals (both ears full, species not
supported) are always shown: they fire once, only when something didn't work, and a silent refusal
is indistinguishable from a broken mod.

## Crafting

Leather, shapeless in any three slots:

```
knife  +  dyed leather  +  beeswax | rendered fat | 0.25 L oil   →   8 tags
```

The tag takes the colour of the leather, so it uses vanilla's existing barrel dyeing — no new dye
mechanics. Oil can be drawn from a bowl, bucket or jug, and accepts vanilla flax (linseed) and
olive oil plus all seven Expanded Foods oils if that mod is installed.

Leather is the right material here: pre-plastic livestock tags really were leather or punched metal,
and the wax/fat/oil rub is genuine leather treatment rather than a hand-wave.

Metal, on an anvil — four small squares beaten out of one hot ingot:

```
##_##
##_##
_____      →   4 tags
##_##
##_##
```

16 voxels of the ingot, so there's plenty left to split away. The recipe accepts the same
21 metals vanilla's own `plate` recipe does, which is its definition of *can be beaten flat* and
exactly what a tag is. Stainless steel and uranium tags exist as items but have no recipe, matching
vanilla — nothing else is smithable from them either.

## Supported animals

| Animal | Status |
|---|---|
| Sheep — mouflon | Tuned in game |
| Sheep — bighorn | Left tuned; right recomputed, recheck |
| Pigs — eurasian, red river, warthog | Left tuned; right recomputed, recheck |
| Pigs — eurasian elder | Derived, not yet checked in game |
| Goats — all 11 breeds | Derived; mountain, markhor, angora seen in game |
| Chickens — hen, rooster, both poults | Leg band; not yet checked in game |

"Derived" means the numbers were computed from the breed's ear box against the hand-tuned mouflon
rather than eyeballed on a live animal. Expect to nudge.

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

Four optional per-species keys cover everything that isn't an ear on a sheep: `shape` and
`shapeMetal` pick different attachment shapes, `terms` picks a different set of lang keys, and
`mirrorZ: false` turns off the Z sign flip for ears where Z centres the tag rather than hanging it.
Both shapes share one set of offsets, so tuning placement stays a single job.

Adding a new species also needs two JSON patches: one giving it the `eartaggable` behaviour, one
declaring the thirty-three tag textures (ten leathers, twenty-three metals). They are split into
`*-eartaggable.json` and `*-eartag-textures.json` under `assets/eartags/patches/` because the
texture half is generated bulk and nobody should have to read past it.

### Porting the mouflon's numbers to another ear

The mouflon is the one entry that was tuned by hand. Everything else comes off it with rules that
hold the tag's **overhang** constant rather than scaling it — a tag is a manufactured object and
ought to be the same size on every animal:

```
offsetY       = earLength - 0.9    tag pokes 0.25 past the tip
offsetZ left  = earWidth  - 0.6    tag hangs 0.5 clear of the lower edge
offsetZ right = -0.6
```

`offsetY` is per side: several ears are modelled 0.1 longer on one side than the other.

**The right ear is not the left ear negated.** "Down" is `+Z` on the left and `−Z` on the right, so
the left measures depth from `Z = 0` while the right measures it from `Z = earWidth`. Negating only
gives the right answer when the ear is exactly `1.2` wide — which the mouflon is, so the mistake
hides on the one animal the numbers were tuned on. The invariant, which holds even for a left value
tuned by eye rather than by the rule above:

```
offsetZ left + offsetZ right = earWidth - 1.2
```

Every entry in `attachpoints.json` satisfies this. `.eartags show` prints both sides so the sum can
be read off in game; the ear widths themselves are listed in the header comment of the config.

Nudging is safe — `.eartags nudge z` moves the sides by `+d` and `−d`, which preserves the sum.

### Goats

All eleven breeds are in. Eight carry a mouflon-shaped ear and just take the two rules above.
The remaining three are thin on Z rather than X, so their tag needs `rotation: [0, 90, 0]` — a
quarter turn about the ear's long axis, which drops the plate *into* the flap instead of across
it. That makes Z a centring offset, hence `mirrorZ: false` on those three.

- `turdag` renders with the `ibexalp` shape, so it borrows the ibexalp numbers.
- `nubian` and `sirohi` pivot their ear bone at the **top**, so Y runs the other way — `0` is the
  tip and the base is up at the ear's full length. Their tags sit high in Y, which is also where a
  real tag would go: up by the head rather than swinging round the jaw.
- `goat-adult.json` declares `texturesByType` for `angora` and `mountain`. The game resolves a
  `*ByType` property by writing the matching branch over its plain sibling, which would drop the
  `textures` block the patch adds — so `goat-eartaggable.json` also adds the ten codes into those
  two branches individually. If the resolver turns out to merge rather than replace, those extra
  ops are harmless duplicates.

### Chickens

Same item and same interaction, different hardware: `assets/eartags/shapes/entity/legband.json` is
a ring of four thin plates round the shank instead of a plate through an ear.

- Both chicken shapes give the shank a `1 x 1` cross-section, so one band definition serves both
  and only the bone name and the height up the leg change.
- The bones are `leg left` / `leg right` on the hen and `L feet` / `R feet` on the rooster — on the
  rooster, `L leg` is the feathered thigh and the bare shank hangs underneath it.
- Both shanks pivot at the top, so Y `0` is down at the foot and Y counts upward.
- The band is `0.9` tall, one and a half of the `0.6` it started at — a single band read as a bead
  at chicken scale. It grows upward from `Y 0`, so `offsetY` still places its bottom edge.
- Poults share the adult shapes at a smaller entity size and need no entry of their own. The chicks
  in `chicken-baby.json` are left alone — they grow up soon enough.
- The band's inner faces stand `0.02` off the leg. Coincident faces z-fight, and a hair of
  clearance is the cheapest fix.

## Live tuning commands

- `.eartags show` — current offsets for the species you're looking at, both sides
- `.eartags nudge x|y|z amount` — shift the tag; `z` is auto-mirrored between ears, unless the
  species sets `mirrorZ: false`
- `.eartags scale n` — resize
- `.eartags save` — write values back to `attachpoints.json`, preserving comments
- `.eartags reload` — re-read the file from disk
- `.eartagfreeze` — pin the animal you're looking at and stop its animations while you tune
- `.eartagmessages` — toggle the apply/remove chat lines, saved to `ModConfig/eartags.json`

Reload and save read/write the config through `Mod.SourcePath`, so they only work while the mod is
an unpacked folder. Zip it and you're back to hand-editing plus a world reload.

`save` rewrites only the `left:` / `right:` lines and leaves every other line — comments included —
exactly as it found them. It writes the key padded out so the two anchors line up, so the matcher
that finds those lines has to tolerate space before the colon; without that it would find a
hand-written file once and then never find its own output again.

## How the shine works

`"reflectiveMode": 4` on every face of `shapes/entity/eartag-metal.json` — the same
`EnumReflectiveMode.Sparkly` vanilla puts on a gold ingot. It's a vertex flag the shader reads, so
it costs nothing at runtime, and it works on entities as well as blocks: vanilla's own gold waist
chain is an entity shape that uses it.

Drop the number to `2` for the duller glint of a plain ingot, or `0` to switch it off. It's
face-by-face, so a find-and-replace on the number is the whole job.

The metal tag is a **separate shape file** rather than a flag on the leather one because
`ShapeElement.Faces` is a `Dictionary<,>`, which a source mod can't touch (see below). Keep the two
geometries in step; placement is shared, since both read the same offsets from `attachpoints.json`.

## How the protection works

`EntityBehaviorEarTaggable.OnEntityReceiveDamage` zeroes the damage when the animal wears a metal
tag and the player is behind it — directly, or as `CauseEntity` behind an arrow.

**The behaviour has to run before `health`.** `Entity.ReceiveDamage` hands the damage to each
behaviour by ref in list order and `EntityBehaviorHealth` is the one that subtracts it, so zeroing
the damage afterwards achieves nothing. The patches therefore insert the *server* copy at
`/server/behaviors/0` rather than appending it — vanilla puts `health` fifth, appending would put
us around twentieth. The client copy only renders the tag, so its position doesn't matter.

`add` at a numeric array index inserting rather than replacing is RFC 6902 behaviour; the game
patches through `Tavis.JsonPatch`, which implements it.

## How the rendering works

The tag is step-parented onto the animal's ear bone at tesselation time — the same mechanism vanilla
uses for the mouflon's mane and the boar's tusks — so it animates with the ear for free. Tag colours
are stored per-side in `WatchedAttributes`, so they sync to clients and survive save/load.

Nothing in the behaviour is ear-specific. The bone, the shape and the wording all come from the
species' entry in `attachpoints.json`, which is how a chicken gets a leg band out of the same code
path and the same texture registration.

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
