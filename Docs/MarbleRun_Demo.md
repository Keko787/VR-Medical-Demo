# Marble run (MarbleRun MiddleVR)

A four-marble race down the inside of the CAVE room. Six troughs spiral anticlockwise down the walls,
so the field circles the viewer twice on the way down and finishes in a catch tray on the floor in
front of them. Wand **button 1** drops the flag; the sign calls the winner.

The point of running it on the walls rather than on a table in the middle of the room is that in a
CAVE the screens *are* the walls. A marble run on a table is something you stand next to; this one
goes past your shoulder, behind your head, and back round again.

## Build it

| Menu item | What it does |
| --- | --- |
| **VR Shooting Gallery ▸ Marble Run ▸ Create Marble Run Scene** | Copies the physics scene to `MarbleRun MiddleVR.unity`, strips the tether demo and the gun out of it, and builds the run |
| **VR Shooting Gallery ▸ Marble Run ▸ Rebuild Marble Run** | Rebuilds the run in whatever scene is open, as long as it has a `CAVE_User_Room` |

The new scene starts as a copy of `Physics MiddleVR.unity` because that scene already carries the
whole MiddleVR side of the setup — `MVRManager` pointed at the CAVE `.vrx`, the `MVR_URP` settings
prefab, and the base camera MiddleVR clones onto its viewports. Rebuilding that from scratch would be
[five separate chances to get it subtly wrong](MiddleVR_CAVE_Setup.md). The copy is written to disk
*before* anything is deleted from it, so the template is never at risk.

Create also adds the scene to **File ▸ Build Settings**, switched off. Tick it to build for the CAVE.

Everything the builder makes lives under `CAVE_User_Room/Marble_Run` and is registered with Undo. A
rebuild clears that subtree, so hand-tuning inside it is lost — tune the constants at the top of
`MarbleRunBuilder.cs` instead.

The builder also owns its materials, but only re-authors the **track** ones (`M_MarbleTrack`,
`M_MarbleRail`) on every build, because their transparency is structural and a stale opaque rail is
the bug that rule exists to prevent. The **marble** colours are written once and then left alone, so
a recolour survives a rebuild.

## The track comes from the room

Nothing is authored in absolute space. The builder reads `Left_Wall`, `Right_Wall`, `Front_Wall` and
`Back_Wall` off the room and lays the troughs relative to those planes, so moving or resizing
`CAVE_User_Room` and rebuilding puts the run back where it belongs. A missing wall falls back to the
stock CAVE dimension with a warning rather than silently building a run in the wrong place.

With the room as it ships (3.556 m × 2.223 m × 2.196 m):

| Leg | Wall | Direction | Bed top |
| --- | --- | --- | --- |
| 1 | Left | back → front | 2.050 → 1.830 |
| 2 | Front | left → right | 1.760 → 1.380 |
| 3 | Right | front → back | 1.310 → 1.082 |
| 4 | Back | right → left | 1.012 → 0.631 |
| 5 | Left | back → front | 0.561 → 0.333 |
| 6 | Front | left → discharge | 0.263 → 0.126 |

13.9 m of trough, 6.5° all the way, about 12–15 s per marble.

## Corners are drops, and that is what keeps it slow

Every leg **ends at the near bed edge of the leg it feeds** and **starts at the far bed edge of the
leg that feeds it**. No two beds ever occupy the same ground, so nothing has to be notched around
anything else, and the marble always leaves at the receiving bed's inner edge travelling straight
across it.

Four pieces make the corner work, over the first 0.30 m of the receiving leg — the **entry**:

- **The catcher** — a 0.18 m wall standing where the receiving leg's outer rail would be. The marble
  crosses the bed in the air (70–90 ms, in which it falls barely 4 cm of the 7 cm corner drop), hits
  this, and drops onto the bed with its old direction spent.
- **The doorway** — the receiving leg's inner rail drops to a kerb across the entry, because that is
  where marbles fly in. An arriving marble's underside crosses that plane a full 6.7 cm up, so it
  sails over.
- **The kerb** — 4 cm, which is above a resting marble's centre. It contains a marble being *shoved*
  sideways without stopping one flying in. This only matters because the start is a race: four
  marbles jostling in one entry pocket can push one of their number out through an open doorway.
- **The end cap** — closes the head of the leg so a marble rattling in the entry cannot roll back out.

The entry is 0.30 m rather than the 0.18 m it needs to span the feeding bed, because the trough is
single-file — 0.14 m clear, which is exactly two marble diameters and therefore not two marbles — so
a race arrives as a queue. 0.30 m holds the whole field nose-to-tail. Shorter, and the back of the
pack has to stack on top of the front of it, which is the one way a marble gets high enough to leave
the trough.

The corner is also the speed governor, and the reason the race is worth watching. Each leg
effectively starts from rest, so no leg exits faster than 2.31 m/s no matter how much track is above
it — a marble that kept its speed through five corners would be doing 5 m/s by the back wall and
would leave the trough at the first one. The same property means a lead built on one leg does not
carry into the next: the pack re-forms five times on the way down, and the order really does change.

## Slope

6.5°, and gentle on purpose. A rolling sphere accelerates at `g·sinθ / 1.4` (the 1.4 is `1 + 2/5`,
the solid sphere's moment of inertia), so 6.5° is 0.79 m/s². Steeper legs reach the corners faster
than the catcher can absorb, and the marble bounces back out of the trough instead of dropping into
it. Raising `k_SlopeDegrees` means raising `k_CatcherHeight` with it.

Rolling without slipping needs only `μ ≥ (2/7)·tanθ` = 0.033 here, so `PM_Marble` at 0.35 friction is
far past the point where the marble skids.

## Seeing it

The rails, catchers, kerbs and basin walls are alpha-blended at **30%** (`k_RailAlpha`); the beds
stay opaque. The marbles spend the whole run on the far side of something — the troughs are bolted to
the walls and watched from inside the room, so the inner rail is always between viewer and marble,
and the 0.18 m catchers are worse. The beds are the exception on purpose: a marble needs a surface to
read against.

The rails are pale rather than the dark grey they started as. At 30% alpha a dark rail reads as a
smear across the marble behind it; a pale one reads as glass. They also stop casting shadows —
a see-through rail throwing a solid shadow would put back the very stripe across the track that
making it see-through was meant to remove.

Transparency is not one property. `SetBlending` writes the surface mode, both blend factors, the
depth-write flag, four shader keywords and the render queue, because that is what the Material
inspector does and a material that has only had its alpha lowered stays stubbornly, invisibly opaque.

## Marbles

Four, 7 cm across, one colour each — RED, AMBER, BLUE, GREEN — so you can follow an individual one
round the room and so the sign can name a winner. Racked nose-to-tail up the first leg, 9 cm apart:
barely more than a marble, so the field starts as a tight pack rather than strung out over half of
the first leg.

`drag` and `angularDrag` are near zero. Both brake a rolling ball directly, and a marble run that
stalls half way down is worse than one that finishes a shade fast. Collision detection is
`ContinuousDynamic`: the beds are 4 cm thick and the marble touches 2.3 m/s, which is 4.6 cm of travel
per 50 Hz tick — discrete detection would let it step through the floor of a trough.

`PM_Marble` is `bounciness 0.2` with `bounceCombine` **Average**. The track carries no physic material
of its own, so the effective restitution at a contact is 0.1 — deliberately dead, because every corner
is a drop and a lively marble spends the whole run rattling instead of rolling. (This is the opposite
call from [`PM_TetherBall`](TetheredBall_Physics.md), which needs **Maximum** for the same reason in
reverse: there the bounce *is* the demo.)

## Starting it

`MarbleRun` on the `Marble_Run` object. Wand **button 1** starts the race; pressing it again re-racks
everything, clears the result and runs from the top, so a demo never needs the Editor to reset it.
Change
`m_StartButton` if your `.vrx` maps things differently — run `MiddleVRWandButtonProbe` to find the
real index, as in [MiddleVR_Controller_Input.md](MiddleVR_Controller_Input.md).

At a desk the wand does not exist until MiddleVR's kernel is up, so in the Editor the matching number
row key (**1**) stands in for the button.

`m_ReleaseMode` decides what a press does. **Race** (the default) lets the whole field go on the same
frame; **Stagger** releases them one at a time `m_ReleaseInterval` apart, which is the old
demonstrate-the-track behaviour. Race is implemented as a zero interval, so the release loop simply
runs straight through the rack in a single frame rather than being a separate code path.

### Calling the winner

`m_FinishZone` is the inside of the catch tray, in the run's own space, filled in by the builder. The
first marble whose centre enters it wins; the sign shows `RED WINS` and holds that until the next
start, so a race that finishes while nobody is watching the sign has still been called when they turn
round. Leave the zone zero-sized and no winner is ever announced — the sign just goes back to READY
once the last marble stops.

It is polled rather than triggered. Four marbles is nothing to test against a box each frame, and it
keeps the finish line out of the physics layer matrix — one less thing to get wrong when the run is
dropped into a scene that already uses triggers.

The press edge is latched inside `MarbleRun` rather than read from MiddleVR's `IsWandButtonToggled`.
The wand is sampled on the cluster's cadence, not Unity's, so that flag can stay set across more than
one frame — which would restart the run twice on a single press.

### Marbles that get away

A marble that jumps a rail lands on the room floor and stays there in plain sight; the next press
collects it. Only a marble that leaves the room entirely (below `m_RecycleBelowY`) is put back on the
rack mid-run, and even then it is racked rather than dropped back onto the track — landing a marble in
the middle of a track someone is watching reads as a glitch.

Release order matters and is not cosmetic. A parked marble is kinematic and therefore solid, so index
0 has to be the one parked furthest *down* the loading ramp; releasing from the back would send the
first marble straight into the ones still waiting.
