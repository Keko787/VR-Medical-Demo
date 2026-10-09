# Head tracking demo (HeadTracking)

A scene with nothing in it but the viewer. The CAVE floor is a sheet of water that ripples from
under your feet — a ring when you arrive, a wake of rings behind you as you walk, a slow pulse while
you stand — and a cloud of bubbles rises from around your head and stays where it was born, so a step
to the side is a step through it. No wand, no gun: the tracker is the only input.

## Build it

| Menu item | What it does |
| --- | --- |
| **VR Shooting Gallery ▸ Tracking ▸ Create Head Tracking Demo Scene** | Copies the physics scene to `HeadTracking.unity`, strips the other demos out of it, and builds the water and the bubbles |
| **VR Shooting Gallery ▸ Tracking ▸ Rebuild Head Tracking Demo** | Rebuilds the demo in whatever scene is open, as long as it has a `CAVE_User_Room` |

The scene starts as a copy of `Physics MiddleVR.unity` for the same reason the
[marble run](MarbleRun_Demo.md) and the [Hot Wheels track](HotWheels_Track.md) do: that scene
already carries the whole MiddleVR side of the setup — `MVRManager` pointed at the CAVE `.vrx`, the
`MVR_URP` settings prefab, the base camera MiddleVR clones onto its viewports — and rebuilding it
from scratch would be several chances to get it subtly wrong. The copy is written to disk *before*
anything is deleted from it. Create also adds the scene to **File ▸ Build Settings**, switched off.

Everything the builder makes lives under `CAVE_User_Room/Tracking_Demo`:

```
Tracking_Demo            HeadRippleSource — turns the head's position into splashes
├── Ripple_Floor         RippleFloor + MeshFilter/MeshRenderer, M_RippleFloor
└── Head_Bubbles         FollowTrackedHead + ParticleSystem, M_Bubble
```

A rebuild clears that subtree, so hand-tuning *inside* it is lost — the numbers live on the
components and in the constants at the top of `HeadTrackingDemoBuilder.cs`. The two materials are
the exception: they are created with the shaders' defaults the first time and then left alone, so
colours tuned in the Inspector survive. The builder also switches off the template's `Ground` plane,
which sits exactly at the water's rest height and would poke up through every trough.

The floor mesh is written to `Assets/_Project/Art/Meshes/RippleFloor.asset` so the Editor has a
floor to show; at runtime `RippleFloor` builds its own copy to animate, and never touches the asset.

## Where the head is

MiddleVR mirrors every `Node3D` of the `.vrx` into a GameObject under the `MVRManager` and writes
the tracker's pose into it each frame, at script execution order −100. The head is the one called
**`HeadNode`**. `TrackedHead` (`Scripts/Tracking/TrackedHead.cs`) finds it by name — the same way
MiddleVR's own `MVRAttachToNode` and `MVRFollowNode` samples do — retrying twice a second until the
kernel has created it, and falls back to `Camera.main` where there is no MiddleVR at all (an Android
build, or Unity XR left on at a desk). Anything that reads it from a plain `Update` sees the current
frame's pose.

In the CAVE the pose is the ART body tracker. At a desk it is MiddleVR's head simulator, which stands
at a sensible height at room centre — so the demo runs at a desk too, it just does not walk.

`FollowTrackedHead` moves in `Update`, not `LateUpdate`, on purpose: particle systems sample their
transform for rate-over-distance emission in `PreLateUpdate`, before any `LateUpdate` runs, so a
follower that moved in `LateUpdate` would feed the emitter last frame's position.

## The water

`RippleFloor` is a height field the size of the floor screen — 3.556 × 2.2225 m at 2.5 cm cells,
143 × 90 vertices — run as a discrete wave equation: each step, every interior cell is pulled toward
the average of its four neighbours, and the velocity that builds up is damped. Every frame the mesh
is rebuilt from it: vertices displaced, normals derived from the neighbours, and the height and slope
written into vertex colour (R and G) for the shader.

It is simulated rather than drawn as rings because interference and reflection are what make water
read as water. A ring that crosses the room, comes back off the front wall and cuts through the next
one is free here; drawing that analytically would mean adding, clipping and fading each ring by hand,
and they still would not cross right.

The step is fixed at 60 per second behind an accumulator. This scheme's wave speed is a fixed number
of cells per step — √(stiffness ⁄ 4), so 0.5 cells at the default — and tying steps to frames would
make the water slow down whenever the frame rate did, which in the CAVE the Editor does, at about
14 FPS. 0.5 cells × 2.5 cm × 60 Hz is 0.75 m/s, about what a pond does. Steps are capped at eight a
frame, so a stall drops time rather than trying to catch up.

| `RippleFloor` field | Default | What it does |
| --- | --- | --- |
| Stiffness | 1 | Wave speed ∝ √stiffness. **Above 2 the scheme blows up**; the slider stops at 1.9 |
| Damping | 0.99 | Velocity kept per step. Lets a ring cross the room and come back before it fades |
| Crest Height | 0.02 m | Where the shader's crest colour saturates; troughs saturate at the same depth |
| Max Height | 0.06 m | Hard cap either way, for the moment two rings meet |
| Foam Gain | 1.5 | How steep a slope has to be before it whitens |

`HeadRippleSource` is what splashes it. The head is dropped straight onto the sheet, so the rings
come from under the feet. Rings are spaced by **distance**, not time — one every 12 cm of travel —
because that is what makes a wake: walk and the rings fall behind you a stride apart, stop and they
stop. The one timed thing is the idle pulse every 1.5 s while standing, and it is there because a
still viewer is the common case in a CAVE and a floor that only answers movement reads as broken to
them. Stepping off the sheet and back on, or a tracking jump of more than a metre in one frame,
counts as arriving again and gets the big splash.

| `HeadRippleSource` field | Default |
| --- | --- |
| Stride spacing / amplitude / radius | 0.12 m / 2.5 cm / 9 cm |
| Idle period / amplitude / radius | 1.5 s / 1.5 cm / 7 cm |
| Arrival amplitude / radius | 4 cm / 14 cm |

### The shader

`VR Demo/Ripple Floor` (`Art/Shaders/RippleFloor.shader`) is a hand-written URP forward shader. It
tints from the vertex colour — trough colour below rest, crest colour above, foam on steep slopes —
lights the surface with the main light and a sharp Blinn-Phong highlight, and adds a Fresnel sheen
at grazing angles. The highlight and the sheen are the view-dependent part: they slide over the
water as the head moves, which is the one thing a surface *at* floor level can otherwise not show,
since a flat floor screen has no parallax whatever the viewer does. The relief adds the rest.

There is no additional-light support because this project's URP asset has additional lights
switched off, and no shadow receiving because a shadow across water reads as a pattern.

### Making it a pool

`k_SurfaceHeight` in the builder is 0: crests and troughs sit either side of the physical floor,
which is what "the floor ripples" means. Sink it — say −0.3 — and the viewer stands on glass over a
pool, with the floor edge as a strong parallax cue. The splash still lands under the feet; the source
projects the head onto the sheet wherever it is.

## The bubbles

`Head_Bubbles` is a `ParticleSystem` on a `FollowTrackedHead`, emitting from a **shell** 32 cm around
the head — wider than a head, so bubbles come into view at the edges rather than in front of the
eyes, where a billboard would be an obvious flat card. Simulation is in **world space**, so the
bubbles stay where they were born and the viewer walks through them. They rise at a steady
6–14 cm/s rather than under negative gravity (which would keep accelerating), wobble on a noise
field, fade in, swell a little, and pop.

Two emission rates, and the second is the positional half of the effect:

| | Rate |
| --- | --- |
| Over time | 28 / s — a sparse cloud while standing |
| Over distance | 40 / m — thickens into a trail as the head moves |

The renderer is **Facing**, not View. A view-aligned billboard turns with each camera's view plane,
and the four CAVE cameras have four different ones, so a bubble straddling a wall seam would be drawn
at two angles. Facing turns it toward the camera *position*, which all four cameras share, so the
seam closes.

`VR Demo/Bubble` (`Art/Shaders/Bubble.shader`) draws the bubble from the billboard's UVs alone — a
soft disc, a thin rim brightest opposite the highlight, a faint glassy body, a highlight dot — so
there is no texture asset to lose. It is additive, weighted by alpha: over the dark floor the
bubbles glow rather than occlude, and additive particles need no sorting, which matters when four
cameras each draw the same cloud. Culling is `AlwaysSimulate`, because automatic culling can pause a
system whose bounds fall off one camera and the four cameras disagree about that.

## Cluster

Nothing here is cluster-synchronised. This CAVE is one machine — every viewport in `MVR.vrx` is
`ClusterNode="0"` — so there is nothing to synchronise with. On a real multi-node cluster the head
pose would arrive on every node through MiddleVR, but the water's step count depends on each node's
own frame time and the bubbles' random seed on each node's own clock, so the walls would drift
apart. The fixes would be `MVRSynchronizeParticleSystem` on the bubbles and a fixed step count per
frame on the floor; neither is worth its cost on a single node.

## Checking it at a desk

Press Play. The MiddleVR preview window shows the room; the water should splash once at room centre
(the arrival), then pulse there every 1.5 s, and the bubbles should rise from head height above it.
The rings should reach the front and side walls and come back. If the floor is flat grey or magenta,
the shader did not compile — the Console will say why, and the builder logs an error rather than
building if `Shader.Find` cannot see it.
