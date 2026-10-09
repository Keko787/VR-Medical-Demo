# Ear hologram (EarHologram)

A right ear, auricle to brainstem, projected in the middle of the CAVE as a medical diagram made of
light: every structure labelled, each one pickable with the wand, the whole thing explodable — and
working. Press **3** and a tone travels through it: sound bands run down the canal, the eardrum and
ossicles rock, the stapes pumps the oval window, and a travelling wave peaks on the cochlea at the
place for that pitch while a magnified cross-section of the cochlear duct shakes beside it. Turn your
head and the semicircular canal that senses the turn lights up.

## Build it

| Menu item | What it does |
| --- | --- |
| **VR Shooting Gallery ▸ Anatomy ▸ Create Ear Hologram Scene** | Copies the physics scene to `EarHologram.unity`, strips the other demos out of it, and builds the hologram |
| **VR Shooting Gallery ▸ Anatomy ▸ Rebuild Ear Hologram** | Rebuilds the hologram in whatever scene is open, as long as it has a `CAVE_User_Room` |
| **VR Shooting Gallery ▸ Anatomy ▸ Render Ear Hologram Stills** | Renders stills of the demo's main states to `Temp/EarHologramStills` — no play mode, no CAVE needed |

The scene starts as a copy of `Physics MiddleVR.unity` for the same reason the other demos' do: that
scene already carries the whole MiddleVR side of the setup. Create also adds the scene to **File ▸
Build Settings**, switched off. From the command line (the Editor must be closed):

```
Unity.exe -batchmode -quit -projectPath . -executeMethod VRShootingGallery.EditorTools.EarHologramStills.RenderBatch -build -stillsDir <dir>
```

builds the scene and writes the stills in one go.

A build takes about ten seconds, most of it meshing the anatomy. Everything lives under
`CAVE_User_Room/Ear_Hologram`, and a rebuild clears that subtree, so tune the constants at the top of
`EarHologramBuilder.cs`, `EarAnatomy.cs` and `CochlearSection.cs`, or the fields on `EarHologram`,
rather than objects inside it. Materials (`Art/Materials/Anatomy/M_Holo_*`) are made the first time
and then left alone, so colours tuned in the Inspector survive. All meshes are written into one
asset, `Art/Meshes/EarHologram.asset` (about 13 MB), updated in place on a rebuild.

```
Ear_Hologram             EarHologram, HearingSimulation, BalanceSimulation
├── Model_Pivot          turns and zooms; kinematic Rigidbody for the moving colliders
│   └── Anatomy          the ear in millimetres, shifted so the framed point sits on the pivot
│       ├── Auricle … Chorda_Tympani      21 HologramParts
│       ├── Sound_Wave_0..2               wavefronts for the hearing animation
│       └── Section_Marker                ring round the cochlea where section A is cut
├── Section_Pivot        the magnified cross-section, shown only when it has something to show
│   └── Section          13 HologramParts, in canal radii
├── Callout, Info_Panel, Projector, Room_Lining, Labels (HologramLabels), Pointer (HologramPointer)
```

## Controls

| Wand | Keyboard | Does |
| --- | --- | --- |
| Trigger (0) | 0 | Select the part under the pointer; pointing at nothing goes back to the overview |
| 1 | 1 | Next part — walks every part outside in, then the section's, then back to the overview |
| 2 | 2 | Explode / assemble |
| 3 | 3 | Hearing: 125 Hz → 500 Hz → 2 kHz → 8 kHz → off |
| 4 | 4 | Labels: main → all → off |
| Stick | Arrows | Left/right turns the model; up/down zooms |
| | Backspace | Reset |

The keyboard works from Unity's keyboard and from MiddleVR's DirectInput one, so the operator's
keyboard in the CAVE drives it too. Button indices are fields on `EarHologram`; check them with
`MiddleVRWandButtonProbe` as in [MiddleVR_Controller_Input.md](MiddleVR_Controller_Input.md).

Selecting a part frames it — the view eases the part to the model's pivot and scales it to about
22 cm across — dims every other part to a ghost, turns the model to the angle the part is best seen
from (the auricle's outer face, the drum looking down the canal, the cochlea along its axis, each
canal face-on to its plane), and writes its description on the panel. While zoomed in, parts more
than half a metre from the focus fade out in the shader, so a cochlea at five times its overview size
does not bring an auricle the size of a door with it. Left alone for 20 s, the model sways gently
round a three-quarter view.

## The anatomy is procedural

There is no imported model. `EarAnatomy` builds the ear in true millimetres in the patient's head
frame — **+X medial, +Y superior, −Z anterior**, eardrum at the origin — so looked at along +Z it is
the textbook coronal view of a right ear. Standard adult dimensions throughout: canal ~25 mm and
S-shaped, eardrum ~9 mm tipped 35°, malleus ~8 mm, stapes ~3 mm, cochlea two and a half turns and
~9 mm across, canals at right angles with the lateral one tipped 30° anterior-end-up.

Nothing is placed absolutely if it can be placed relative to what it touches. The ossicles hang off
the eardrum's frame, the stapes off the incus, the vestibule off the footplate, the canals and cochlea
off the vestibule, the nerves off those. That is what keeps the middle ear 4 mm deep, the incus'
long process just behind the malleus handle, and the stapes visible through the posterosuperior
eardrum exactly where an otoscope finds it.

Organic parts are **signed distance fields meshed by surface nets** (`SdfShape`): round-cone chains,
ellipsoids and a height-field sheet, smooth-unioned. A malleus made that way reads as one bone rather
than a kit of capsules. The bony labyrinth is meshed as a single surface and split into cochlea,
vestibule and three canals by nearest primitive, so their seams are exact but each can still be
lit and picked on its own. The auricle is a thin cartilage sheet draped over gaussian bumps and
ridges (concha, antihelix, fossae), with the helix, antihelix, tragus, antitragus and lobule
smooth-unioned on and the canal's opening cut through the concha. Tubes (canal, facial nerve,
Eustachian tube) are plain sweeps with rotation-minimising frames.

Each vertex carries `u` — how far along its structure it is: base to apex on the cochlea, ampulla
round on a canal, organ out to brainstem on a nerve. The animation runs on it.

### The cross-section

`CochlearSection` is a thin (±6°) slice of the cochlear duct at the **2 kHz place**, magnified about
270×: scala vestibuli, media and tympani, Reissner's and the basilar membrane, the organ of Corti
with one row of inner and three staggered rows of outer hair cells and their stereocilia, the
tectorial membrane, stria vascularis, spiral ligament, spiral lamina and limbus, and the spiral
ganglion with its fibres. Each tissue is a 2D profile turned about the modiolus, so the slice has the
spiral's real curvature. It is thin on purpose: a hologram's outlines glow where a surface is seen
edge-on, and a thick wedge smears every boundary across its depth. It comes on stage — the model
stepping aside for it — when the cochlea or one of its own parts is selected, or a tone is playing.

## How it works, animated

`HearingSimulation`. Nothing moves at the tone's real frequency; every mechanical motion runs at a
**visual rate** of 1–4.5 Hz that rises with the tone, with amplitudes exaggerated about a
thousandfold. What is kept true is the causal chain, the phase relationships, and where each tone
peaks.

- **Canal** — sound bands travel medially along its `u`; wavefront rings close in on the concha.
- **Eardrum** — vibrates in the vertex shader along its normal, most at the umbo.
- **Ossicles** — the malleus and incus rotate *together* about the ossicular axis (anterior process
  of the malleus to short process of the incus) by just enough that the umbo tracks the drum. The
  stapes is pushed by however far that carries the tip of the incus, projected on its own axis — so
  the piston's size and phase, lever ratio included, come out of the geometry.
- **Windows** — the oval window moves with the footplate; the round window bulges out as it goes in.
- **Cochlea** — a travelling wave runs from base to apex with its peak where the Greenwood function
  puts that frequency, `x = log₁₀(f / 165.4 + 0.88) / 2.1` from the apex: 125 Hz near the apex,
  8 kHz near the base. The envelope builds slowly and dies within a few percent past the peak.
- **Section A** — shakes in proportion to that envelope at the section's place: hard at 2 kHz, a
  little at 125 and 500 Hz (their waves pass through it on the way up), not at all at 8 kHz (its wave
  has died before it gets there). The tectorial membrane travels half as far as the organ beneath
  it, and that shear is what bends the stereocilia; the hair cells glow on the upswing.
- **Nerve** — pulses run out along the cochlear nerve and CN VIII.

A pure sine plays at the real frequency (1 s clips of a whole number of cycles, so they loop without
a click). If MiddleVR's cameras leave the scene with no active `AudioListener`, one is added. The
info panel reads out a six-step walkthrough, and while it runs the labels show only the parts the
current step is about.

`BalanceSimulation` takes the viewer's head rotation from the tracker, in the head's own frame, turns
it into the right ear's anatomical frame (a half turn about Y — head right is the model's lateral),
and projects it onto each canal's plane normal. Shake your head "no" and the lateral canal lights;
nod and the vertical canals do; the 30° tilt means a pure yaw also stirs the anterior canal a little,
as it does for real. Endolymph bands flow round an active canal *against* the turn, and the vestibular
nerve fires. Below 15°/s nothing lights — tracker jitter lives down there.

## Labels

`HologramLabels` lays labels out like an illustrator: two columns either side of the model, stacked
so they never overlap, leader lines to their structures. The layout is redone every frame in a plane
facing the tracked head, and the columns stand just clear of the visible parts' projected bounds, so
they hug a zoomed cochlea as closely as they clear the whole auricle. Text faces the head's position,
not a camera plane, so it does not kink at a CAVE wall seam.

Parts have a label **tier**: the overview shows the major structures, with "Ossicles" and
"Semicircular canals" standing in for their members; zooming in or exploding brings up the
individual bones, canals and nerve branches; the All mode adds the small ones (oval and round
windows, utricle, saccule, chorda tympani). A selected part shows its landmarks — helix, antihelix,
concha, tragus… on the auricle; umbo on the drum; base and apex, with their pitches, on the cochlea.

## Look

`VR Demo/Hologram` is additive, two-sided and unlit: a faint tinted body with a Fresnel rim, so parts
read by their silhouettes and you see straight through to what is behind — the ossicles through the
drum, the modiolus through the cochlea. Additive needs no sorting across the four CAVE cameras.
Scanlines are horizontal bands in **world** Y and the scan sweep rises through the room, so the walls
agree at their seams and both eyes see the same bands. Per-part state (highlight, glow, visibility,
signal along `u`, membrane vibration, fade at a nerve's far end) arrives through a
`MaterialPropertyBlock` that `HologramPart` composes once per frame from everything that wants a say.

The room is lined, a few millimetres inside the template's walls, with `VR Demo/Hologram Grid`: near
black with a faint blueprint grid and a pool of light round the projector. The hologram's light needs
black to land on; the template's grey screens would wash it out. `VR Demo/Hologram Additive` draws
the leader lines, the wand's beam, the projector's rings and cone and the wavefronts, and
`VR Demo/Hologram Panel` the info card.

Colour follows systems, not realism: outer ear cyan, membranes rose, bone amber, cochlea violet,
vestibular apparatus periwinkle, nerves yellow, facial nerve orange, airway green.

## Checking it at a desk

Press Play. The MiddleVR preview shows the room. Number keys and arrows drive it; at a desk there is
no wand, so no pointer beam, and the canals only light if the head simulator turns. The quickest
check of any change is **Render Ear Hologram Stills**: fifteen views, from the overview to the
2 kHz section and a head shake, straight from the Editor.

## Cluster

Nothing here is cluster-synchronised. This CAVE is one machine — every viewport in `MVR.vrx` is
`ClusterNode="0"`. On a multi-node cluster the animations, which run on `Time.time`, would drift
between nodes; they would need MiddleVR's synchronised clock.
