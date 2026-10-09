# VR Medical Demo — CAVE anatomy demos for the VisCube M4

Unity demos for the AERI Lab's **VisCube M4 CAVE** at the University of Missouri: three projection
walls plus a floor, driven by **MiddleVR**, with one tracked viewer and one wand.

The project began as a stationary VR shooting gallery for the Quest and was ported to the CAVE. This
repository carries it on as a medical demo: anatomy shown as a working hologram in the middle of the
room, which you can walk round, take apart and set running.

| | |
| --- | --- |
| **Unity** | 2022.3.9f1 LTS |
| **Render pipeline** | URP 14.0.11 |
| **VR** | MiddleVR 2.4.0 — *not* Unity XR; OpenXR is disabled on purpose |
| **Room** | 3.556 × 2.2225 m screens, 3 walls + floor (`MVR.vrx`) |

## The demos

Each lives in its own scene under `Assets/_Project/Scenes/Demos/`, and each has a build menu item
under **VR Shooting Gallery ▸ …** that generates the scene from a template, plus a document.

| Scene | What it is | Doc |
| --- | --- | --- |
| `EarHologram` | A right ear from auricle to brainstem as a labelled hologram: pick parts with the wand, explode it, send a tone through it and watch the cochlea sort it by pitch, turn your head and watch the canals sense it | [EarHologram_Demo.md](Docs/EarHologram_Demo.md) |
| `HeadTracking` | The floor ripples under the viewer and bubbles rise round their head — tracking, felt | [HeadTracking_Demo.md](Docs/HeadTracking_Demo.md) |
| `ShootingGallery MiddleVR` | The original shooting gallery round | [ShootingGallery_Game.md](Docs/ShootingGallery_Game.md) |
| `Physics MiddleVR` | Tethered-ball physics; also the template the other scenes are copied from | [TetheredBall_Physics.md](Docs/TetheredBall_Physics.md) |
| `MarbleRun MiddleVR` | Marble run | [MarbleRun_Demo.md](Docs/MarbleRun_Demo.md) |
| `HotWheelsTrack` | Slot-car track | [HotWheels_Track.md](Docs/HotWheels_Track.md) |

## Getting it running

Porting a Unity project to this CAVE has a chain of five things that must all be true before a
single pixel appears — the MiddleVR version guard, the scripting backend, the URP companion
package, disabling OpenXR, and having a plain Main Camera for MiddleVR to clone. All of it, in
order, with the reasoning:

> **[Docs/MiddleVR_CAVE_Setup.md](Docs/MiddleVR_CAVE_Setup.md)**

Pressing Play on a dev PC shows MiddleVR's *preview window*, not the walls. The CAVE itself only
lights up when the app runs on the CAVE system. For the ear hologram there is a shortcut:
**VR Shooting Gallery ▸ Anatomy ▸ Render Ear Hologram Stills** renders every state of the demo from a
standing viewer's eye, straight from the Editor.

Other setup notes: [MiddleVR_Controller_Input.md](Docs/MiddleVR_Controller_Input.md) for the wand's
button indices, [MiddleVR_A6000_GPU_Placement.md](Docs/MiddleVR_A6000_GPU_Placement.md) for the
render nodes.

## Layout

```
Assets/_Project/
├─ Art/            Materials (Anatomy/ for the hologram), generated meshes, and hand-written URP shaders
├─ Scenes/         Boot/ and Demos/
└─ Scripts/
   ├─ Editor/      Scene builders — each demo is generated, not hand-placed
   │  └─ Anatomy/  The ear's geometry: signed-distance meshing, sweeps, the anatomy and the cochlear section
   ├─ Anatomy/     The hologram at runtime: parts, labels, pointer, hearing and balance simulations
   ├─ Tracking/    Head and wand from MiddleVR's nodes; the head-tracking demo
   ├─ Gun/  Targets/  Core/  UI/ The shooting gallery
   └─ Physics/     Marble run, slot cars, tethered ball
```

Demo scenes are **built by editor scripts**, not assembled by hand: a builder copies the template
scene, strips the other demos out of it and generates the geometry, so layout and tuning live in
constants at the top of one file rather than in a scene nobody can diff. A rebuild keeps materials
tuned in the Inspector; anything changed by hand inside a generated object is rebuilt away, so tune
the builder's constants instead.

The ear's meshes are generated too, from anatomy written in millimetres, and saved to
`Art/Meshes/EarHologram.asset` (about 13 MB) so the scene opens without a rebuild.

## Design constraints

The room shapes everything, and these are not negotiable:

- **One correct viewpoint.** Perspective is solved for the tracked user; everyone else sees a
  skewed frame. The hologram's labels face the tracked head, so the person holding the wand reads
  them square on.
- **One wand.** One-handed interaction only. Everything the hologram does is on the wand's buttons
  and stick, and on the number keys for an operator.
- **The room is the stage, not a window.** Geometry crossing a screen edge breaks the illusion for
  the whole room, and content past the physical walls has no surface to land on — which is why the
  hologram fades whatever falls outside its focus when you zoom in.
- **No virtual locomotion.** The user walks physically. Cybersickness and, in a real room, safety.

## Notes

The shooting-gallery-era planning documents — [DESIGN.md](DESIGN.md),
[IMPLEMENTATION.md](IMPLEMENTATION.md) and the phase checklists — describe the original Quest build
and are kept for provenance; the CAVE demos above superseded that plan.
