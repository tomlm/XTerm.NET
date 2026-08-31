# tools

## kitty-demo.ps1

Walks through the Kitty graphics protocol feature by feature, pausing between each so the screen can
be looked at. Run it **inside** the terminal under test — it emits raw escape sequences and depends
on nothing else.

```
pwsh -File kitty-demo.ps1                  every step
pwsh -File kitty-demo.ps1 -Only overlap    just that one
pwsh -File kitty-demo.ps1 -NoPause         straight through
```

Steps: `detect` `place` `scale` `crop` `delete` `behind` `overlap` `reveal` `placeholder` `animate`
`stack-anim`.

The three that exercise overlapping placements:

- **`overlap`** — a translucent panel over half a photograph. The picture shows *through* it, which
  it cannot do unless the cell kept both and they were drawn bottom-up.
- **`reveal`** — an opaque picture over the middle of another, then deleted. The one behind comes
  back whole. Before overlap was implemented the covered cells had been overwritten, so it came back
  with a hole through it — and that happened to opaque pictures too.
- **`stack-anim`** — a running animation layered on a still picture, so the frame-by-frame texture
  re-upload and the layering are exercised together.

Two photographs rather than flat rectangles throughout: a solid colour hides a shuffled tile, a strip
drawn from the wrong row, or a picture stretched by a couple of pixels — and hides a blend entirely,
since a tint over one flat colour is just another flat colour.

## vttest/

Drives [vttest](https://invisible-island.net/vttest/) against this emulator headlessly, and against
tmux as a second opinion, then reports where the two screens differ.

```
dotnet build vttest/VtDrive/VtDrive.csproj

python vttest/vtsweep.py 8 12                        walk menu 8, twelve RETURNs
python vttest/vtsweep.py --keys - "11" "8" "2"  drive a sub-menu explicitly
```

`VtDrive` spawns vttest through a pty into a headless `Terminal`, wires `DataReceived` back to the
pty so reports can be answered, wires `Resized` to `connection.Resize` so DECCOLM reaches the
application, and dumps the screen after each step. Run it alone to read one screen:

```
dotnet run --project vttest/VtDrive -- - "6" "3"
```

**It nominates; it does not judge.** vttest states its own verdict for the tests worth trusting
(`-- OK`, `-- Ignores origin mode`, `expected EAED`), and that is what decides which side is wrong —
several differences this found were tmux's bugs, not ours. tmux has no VT52, ignores DECCOLM,
reports untranslated characters for line-drawing and national sets, and implements neither DECRQM
nor DECRQCRA, so those menus differ whatever we do.

It is also blind to everything that is not text — colour, BCE fills, cell protection, line
attributes. Those need a direct probe against the emulator, which is where
`XTerm.NET.Tests/VtTestBehaviourTests` came from.

Needs `vttest` and `tmux` inside WSL. The findings this produced are issues #123-#126 and #128-#132,
with the cases that judge themselves ported into `XTerm.NET.Tests/VtTestConformanceTests`.

## Assets

Everything here is **CC0 or public domain** and safe to redistribute. All of it comes from Wikimedia
Commons, where the licence is recorded against the file and can be checked.

| File | What | Used by | Source | Licence |
| --- | --- | --- | --- | --- |
| `kitten-grass.png` | 320×213 | most steps | [Domestic shorthair cat portrait in grass](https://commons.wikimedia.org/wiki/File:Domestic_shorthair_cat_portrait_in_grass.jpg) | CC0 |
| `kitten-dark.png` | 320×240 | `behind`, `overlap`, `reveal` | [Cute Kitten sleeping on bed](https://commons.wikimedia.org/wiki/File:Cute_Kitten_sleeping_on_bed.jpg) | CC0 |
| `kitten-tile.png` | 120×144 | `placeholder` | [Tabby cat with blue eyes](https://commons.wikimedia.org/wiki/File:Tabby_cat_with_blue_eyes-3336579.jpg) | CC0 |
| `anim/f01…f20.png` | 200×90, 20 frames | `animate`, `stack-anim` | [Cat trotting, changing to a gallop](https://commons.wikimedia.org/wiki/File:Cat_trotting,_changing_to_a_gallop.gif) — Eadweard Muybridge, 1887 | Public domain |

The animation is a Muybridge locomotion study, which is close to ideal for the purpose: the whole
point of the sequence is that every frame differs from its neighbours in a way the eye checks
without being asked, so a dropped, repeated or reordered frame is obvious rather than plausible.

`kitten-tile.png` is small for a reason. The `placeholder` step cannot scale what it shows — a
virtual placement's `c` and `r` are parsed and then dropped, so the picture covers its *natural*
number of cells and the only way to get a sensible size on screen is to send a sensibly sized
picture. Fix that gap and this asset can go.

The only flat image left is the translucent tint in the `overlap` step, built in the script rather
than stored. That is the one place a featureless picture is the right one: a tint has to have no
detail of its own or you cannot tell which of the two layers you are looking through.

## Cell size

The script asks the terminal how big a cell is with `CSI 16 t` and falls back to 10x20 when there is
no answer within half a second — which is what happens when the host does not route the reply back,
and it happened while this was being written. Everything that can be is therefore placed with `c=`
and `r=`, naming a box in *cells*, so a wrong answer changes nothing. Two things cannot be:

- `placeholder`, because a virtual placement's `c`/`r` are dropped, so the grid of placeholder cells
  has to be computed from the pixel size and the cell size.
- the pixel dimensions of the generated tint, which only decide its resolution — it is placed with
  `c`/`r` like everything else.

All were downscaled and requantised from the originals. They are PNG so they go over the wire as
`f=100` and the terminal does the decoding; being 8-bit colormap, they also walk the indexed and
`PLTE` branches of the decoder, which the raw-RGBA pictures the script builds itself never reach.

Swapping any of them needs no code change — `kitty-demo.ps1` reads the pixel size out of the PNG
header, and the animation step takes whatever frames are in `anim/`.
