# Hot Wheels slot track (HotWheelsTrack)

A two-player slot-car circuit built inside the CAVE room. It starts on the floor in front of the
viewer, jumps a gap, spirals a turn and a half up the right-hand wall, comes back overhead along the
front wall through a barrel roll and a vertical loop, and plunges down the left-hand wall to the grid
again. **Player one drives with the wand trigger, player two with an Xbox trigger.** First to five
laps wins; the sign calls it.

13.4 m of track per lap, about 2.9 s a lap flat out.

## Build it

| Menu item | What it does |
| --- | --- |
| **VR Shooting Gallery ▸ Hot Wheels ▸ Create Hot Wheels Track Scene** | Copies the physics scene to `HotWheelsTrack.unity`, strips the other demos out of it, and builds the circuit |
| **VR Shooting Gallery ▸ Hot Wheels ▸ Rebuild Hot Wheels Track** | Rebuilds the circuit in whatever scene is open, as long as it has a `CAVE_User_Room` |

The scene starts as a copy of `Physics MiddleVR.unity` for the same reason the
[marble run](MarbleRun_Demo.md) does: that scene already carries the whole MiddleVR side of the setup
— `MVRManager` pointed at the CAVE `.vrx`, the `MVR_URP` settings prefab, the base camera MiddleVR
clones onto its viewports — and rebuilding it from scratch would be several chances to get it subtly
wrong. The copy is written to disk *before* anything is deleted from it, so the template is never at
risk. Create also adds the scene to **File ▸ Build Settings**, switched off.

Everything the builder makes lives under `CAVE_User_Room/Hot_Wheels_Track` and is registered with
Undo. A rebuild clears that subtree, so hand-tuning inside it is lost — change the constants at the
top of `HotWheelsTrackBuilder.cs` instead. The track mesh is written to
`Assets/_Project/Art/Meshes/HotWheelsTrack.asset` and rewritten **in place** on each build, so
anything already pointing at it follows the rebuild.

## The CAVE decides where the track can be

This is the constraint the whole layout is shaped around, and it is worth stating plainly because it
is not obvious from inside the Editor.

`MVR.vrx` defines **four** screens: `ScreenCenter` (the front wall), `ScreenFloor`, `ScreenLeft` and
`ScreenRight`. **There is no back screen.** A point in the room is only visible if the line from the
viewer's eye through it lands on one of those four surfaces — so anything behind the viewer at head
height projects onto the one wall that has no projector, and simply is not there. The rear of the
room above floor level is a dead zone.

So the circuit lives entirely in **z ∈ [-0.09, 0.95]** — the front half of the room:

| | Extent | Nearest surface | Clearance |
| --- | --- | --- | --- |
| Across | -1.58 … 1.58 | side walls at ±1.778 | 0.10 m of ribbon edge |
| Up | 0.05 … 1.82 | ceiling at 2.2225 | 0.37 m |
| **Back** | **-0.09** | **back wall at -1.111** | **0.92 m** |
| Front | 0.95 | front wall at 1.111 | 0.06 m |

Only the back straight is behind room centre at all, and it is flat on the floor, where the floor
screen covers the whole room depth. Everything with any height to it — the spiral, the high straight,
the loop, the plunge — is either forward of the viewer or hard against a side wall, which is exactly
where the three vertical screens are.

`CheckRoom` re-derives all of this from `Left_Wall`, `Right_Wall`, `Front_Wall` and `Back_Wall` on
every build and **refuses to build** a circuit that would cross the back wall, rather than trusting
the numbers above to stay true if the room is ever resized. It also refuses a grid too short to clear
the jump from a standing start, and a front straight too short for the loop and the roll.

## The lap

Nothing is authored in absolute space. The two ends are half-turns of the same radius about centres
on the same line, so the circuit closes **by construction** — measured closure error is 0.1 mm, which
is the baking step, not a fudge.

| | Element | Where |
| --- | --- | --- |
| 1 | Grid and run-up, 0.60 m | Floor, back straight |
| 2 | **Jump** — ramp to 13°, 0.16 m flat gap, descending landing ramp | Floor, back straight |
| 3 | **Spiral** — 540°, climbing 1.05 m at 11.5°, banked 38° | Right wall |
| 4 | **Barrel roll** — 0.50 m, surface turns 360° | Front wall, 1.10 m up |
| 5 | **Vertical loop** — 0.36 m radius, tops out at 1.82 m | Front wall, 1.10 m up |
| 6 | **Plunge** — 180°, dropping 1.05 m at 27.4°, banked 55° | Left wall |

### Why a 540° spiral drops into a 180° corner

The trick that makes the whole thing fit. A 540° turn **leaves in the same place and pointing the
same way** as a 180° turn of the same radius about the same centre, because the two differ by a whole
revolution. So a turn and a half of climbing spiral can be substituted for a plain half-turn without
disturbing anything downstream of it — which is what buys 5.0 m of gentle 11.5° climb where a half
turn would have given 1.6 m and needed 33°.

The grades are then solved rather than guessed. `SolveGrade` bisects for the climb angle at which the
easing ramp in, the helix itself and the easing ramp out add up to exactly the height the far end
needs — because the ramps contribute `R(1 − cos θ)` and the helix `L·tan θ`, which does not rearrange
into anything worth reading.

### One segment type

`TrackSegment` has four channels and every element above is some combination of them:

- **`TurnDeg`** — yaw swept about the **world** vertical. World, not the frame's own up: yawing about
  a frame that is pitched into a climb traces a tilted circle, not a rising one.
- **`LoopDeg`** — pitch swept about the frame's **local right**, which carries a loop past the
  vertical where a heading-and-grade description would gimbal-lock.
- **`GradeDeg`** — a climb angle *held* rather than accumulated, so a long spiral cannot drift off
  the angle it was authored with. A segment with `LoopDeg` set carries its incoming pitch instead —
  that pitch is what it is sweeping — which makes the ramps either side of the jump the only pieces
  that ever change the grade.
- **`BankIn/OutDeg`** — roll of the running *surface* only; the path ignores it. That separation is
  what lets a barrel roll be a straight piece whose surface happens to turn over, rather than a
  corkscrew a car would have to be flung around.

Only the segments are serialized. The centre line is re-integrated in `Awake` by the same code the
builder walks to lay out the mesh, so the track a car drives and the track a player sees cannot drift
apart, and the scene file stays a few dozen lines of description rather than a thousand baked
transforms.

Two sign conventions are easy to get backwards and both were, once: **negative `TurnDeg` turns toward
+Z**, and the pitch axis is `Cross(forward, up)` — the *left* — because Unity turns a positive angle
about the frame's right into a nose-*down* pitch, and a take-off ramp needs positive to mean up.

### Banked corners are three pieces

Each banked turn is laid down as entry / hold / exit so the lean builds through the entry and lets go
through the exit. This is not cosmetic. Bank tilts the running surface, and a tilted surface turns
*any* load — including the purely vertical one of pulling out of a dive — partly sideways onto the
slot. Carrying 55° of lean into the straight ramp at the foot of the plunge made that pull-out the
single heaviest thing in the lap, which is both wrong and the opposite of what banking is for. The
three pieces integrate exactly as one piece did, so closure is unaffected.

### The gap has to be flat

The jump was originally built as a take-off arc, a gap that carried on climbing at the take-off
angle, and a landing arc. It could not be cleared at any speed, and the reason is worth keeping:

> A projectile launched at angle θ is always **below** the straight line at angle θ from the same
> point.

So a gap that keeps climbing at the take-off angle puts its far lip six centimetres above where the
car left — permanently out of reach — and every car flew into the *underside* of the ramp it was
supposed to land on. The gap is now flat: the landing lip sits at exactly lip height and the ramp
falls away from there, so anything that crosses the hole lands on something.

That makes the climb before the lip a real piece of track (`Jump_Ramp`) rather than a by-product of
the gap, and it has to be longer than the descending ramp on the far side by `R·tan(θ/2)` — one arc
lifts the car and two put it down, so the climb pays for the extra arc. Otherwise the back straight
comes out of its own jump lower than it went in, and the lap stops closing.

Two consequences in `SlotCar`:

- **The car launches from the lip, not from the gap.** The line through a gap is bookkeeping — it
  carries arc length across the hole so the lap still measures, and it is now deliberately flat,
  which is exactly why the launch direction must not come from it. The car leaves along the last
  track it was actually standing on, and is moved to that lip first: a frame at 5 m/s is eight
  centimetres, and a jump that starts eight centimetres short of its own take-off is one whose
  difficulty depends on the frame rate.
- **Landing means coming down to the track, not being near it.** Testing proximity put the car back
  in the slot the moment it drew level with the landing ramp — the jump existed in the geometry and
  never once in the air. Height above the running surface is the honest test and the robust one:
  any tolerance tight enough to prevent an early snap would be loose enough to let a fast car step
  clean over the ramp between two frames.

Ramp angle is the one number to touch if the jump wants retuning, and it is a genuine trade. Range
goes as `v²·sin(2θ)`, and a car arriving off the plunge at 5.4 m/s covers three times the ground one
leaving the grid at 3.3 does — 13° gives about 0.6 m and 30 mm of air from the grid, and 0.9 m with
70 mm later in the race. Steeper reads better and starts landing cars in the spiral; shallower keeps
everything on the straight and stops looking like a jump.

### The mouth of the loop

A vertical loop reaches `radius` *behind* where the car enters it as well as ahead, and its last few
degrees lie in the same plane as the straight it rises out of — which fights that straight for depth.
The first and last 20° are therefore built but not drawn (`Hidden`, as distinct from `Gap`: the car
stays in the slot). A real loop has the same opening at its foot for the same reason.

## Driving

`SlotCar` rides the centre line as a distance and a speed rather than as a rigidbody. That is what
makes a loop and a barrel roll survivable at all — a free body would have to be flung round them at
exactly the right speed, and a demo cannot be one bad frame away from a car in the audience.

Everything that makes it feel physical is still real, because it is all longitudinal. Gravity is the
tangent's own rise, so the spiral genuinely costs speed, the plunge genuinely gives it back, and the
car slows over the top of the loop and accelerates out of it without any of that being scripted. Over
the jump the car leaves the slot entirely and flies a real ballistic arc, and only the part of its
landing speed that was going the track's way survives the landing — so a car that drops nose-first
out of a high arc comes out slower than one that skims the ramp, with none of that special-cased.

### Ways to lose it

| | Cause | Recovery |
| --- | --- | --- |
| **Missed jump** | Under about 1.5 m/s at the lip | Tumbles, back on 0.9 m upstream after 1.5 s |
| **Stuck in the loop** | Under about 3.8 m/s entering it | Same, after 2.5 s of no progress *with the throttle open* |
| De-slot | Cornering above `m_SlotGrip` | Same |

Two of the three are on the too-slow side, deliberately. The only corner fast enough to matter is the
foot of the plunge, and its speed comes from the 1.05 m drop above it rather than from the throttle —
so a player cannot drive around a de-slot there however carefully they lift, and it would read as the
track being broken rather than as their mistake. The circuit peaks at **5.7 g**, `m_SlotGrip` ships at
60 m/s², and so cornering never throws anyone out. **Drop it to about 45 and the entry to the spiral
becomes a corner you have to lift for** — that field is the difficulty dial.

The loop cannot simply drop a car that is too slow for it, because the slot holds an inverted car —
that is what a slot is, a pin in a groove rather than a hope, and it is not a concession either, since
a barrel roll is *straight* and a straight piece generates no centripetal force to hold anything to it
at any speed. What a too-slow car does instead is roll back down the near side, climb again, and rock
there indefinitely, which is worse to watch than falling out. `Stuck` measures progress rather than
speed — a car oscillating in a loop is never actually stationary — and lets go after 2.5 s, landing in
the same place a player expects.

It only counts while the throttle is open, and that qualifier is not a detail. Without it, a car
sitting on the grid waiting for someone to pick up a controller is also making no progress, and gets
thrown off the track a few seconds into every scene, put back, and thrown off again — which looks
from the outside like two cars that cannot make the opening jump. Standing still because nobody has
asked for anything is not being stuck.

`m_TopSpeed` is set by the loop rather than by taste: a car needs `sqrt(4·g·r)` ≈ 3.8 m/s merely to
reach the top of it, so half throttle has to be comfortably above that or a player feathering an
analog trigger could never get round at all.

### Lanes

Both cars are timed on the **centre line**; the lane offset only decides which side of it a car is
drawn. So neither lane is quicker, with none of the lane-swap crossovers a real two-lane track needs
to be fair — and no risk of two cars occupying the same piece of track at a crossover, which is what
those cost.

## Controls

| Action | Player one | Player two |
| --- | --- | --- |
| Throttle | Wand **button 0** (the trigger) | Xbox **either trigger** (analog), or **A** |
| Keyboard stand-in | **Left Shift** | **Right Shift** |
| Re-rack the grid | Wand **button 1**, or **R** | |

The wand's trigger is a button — the `.vrx` turns the analog pull into a digital press — so player one
gets a hard on/off while player two gets fine control. That asymmetry is left in rather than levelled
out: squeezing an Xbox trigger half way is the natural thing to try, and taking it away to be fair to
the wand would make the better controller feel broken. It is also the older, truer slot-car
arrangement, where the two hand controllers were never identical either.

A pad reaches Unity by one of two roads and which one is not up to the script: Unity's Input System
claims it, or MiddleVR's DirectInput driver does (`MiddleVRJoystick`, the twin of `MiddleVRWand`).
**Both are read and the larger pull wins**, so the same build works whichever way the machine is set
up, with no configuration to get wrong. The keyboard stand-ins are live in built players too, not just
in the Editor — if a pad goes missing five minutes before a demo, the operator can still run two cars.

Player two's throttle takes whichever of the two triggers is pulled harder — being opinionated about
left versus right buys nothing, and **A** is there as an escape hatch for a pad whose triggers arrive
as an axis Unity does not recognise. A pad Windows does not report as XInput arrives as a plain HID
stick instead, where `Gamepad.current` stays null however hard the trigger is pulled; its primary
trigger is read as well, which is enough to drive a car.

Wand button indices are per-`.vrx`; run `MiddleVRWandButtonProbe` to find the real ones, as in
[MiddleVR_Controller_Input.md](MiddleVR_Controller_Input.md).

## When a controller does not work

`ControllerSitRep` sits in the built scene and logs a full report at startup, on **F1**, and whenever
the device list changes or the MiddleVR kernel comes up or goes away. It exists because "the pad does
not work" has at least four causes that look identical from the outside:

| What the report shows | What it means |
| --- | --- |
| No device listed at all | Nothing is plugged in, or Windows has not enumerated it |
| A `Joystick` listed, `Gamepad.current — NONE` | Windows is not reporting it as XInput; its trigger still drives a car, but the analog range is gone |
| `Gamepad.current — NONE` and a MiddleVR joystick listed | MiddleVR's DirectInput driver has claimed the pad; use the `m_Joystick*` fields |
| Everything present, throttle still `0.00` | It is wired to the wrong axis or button index |

The second half is the live change log, and it is the half that answers *which index*: pull the
trigger and the line that appears names the control and the number to put in the Inspector — the same
trick `MiddleVRWandButtonProbe` uses for the wand.

```
[SitRep] MVR joystick 0 axis 2: -1.00 -> +0.35   (set m_JoystickAxis to 2 to drive a car with this)
[SitRep] Gamepad rightTrigger: 0.00 -> 0.62
```

It is silent unless something moves, so it is left in the scene rather than added when something
breaks — the startup report is worth having in the log of a demo that went fine as much as one that
did not.

### A MiddleVR axis is not assumed to rest at −1

`MiddleVRJoystick.Trigger` clamps rather than rescaling `-1..1` onto `0..1`. Both conventions exist,
and rescaling to support triggers that rest at −1 breaks the ones that rest at 0 badly: an axis
resting at 0 would come back as **half throttle**, and the car it drives would pull away on its own
and never stop. Clamping is right for a resting-at-0 trigger and merely wastes the unused half of a
resting-at-−1 one — it costs a little travel and cannot run away.

## Starting it

There is no countdown and no start gate. Pulling your trigger drives your car from the first frame the
scene is up, which is what a visitor handed a controller in a CAVE actually tries, and being told to
wait for a light is a worse first ten seconds than simply going. The clock starts on its own when a
car first moves.

Crossing the line for the last lap does not stop anybody either — the winner is called and held on the
sign, and both cars stay drivable, because the demo is more often two people playing than a contest
anyone is scoring. Wand button 1 re-racks the grid.

## Look

The track is opaque throughout, which is the one real difference from the
[marble run's](MarbleRun_Demo.md) palette. There the marbles spent the whole run behind a rail and the
rails had to be see-through; here a slot car sits on top of its track and nothing is ever between it
and the viewer.

Hot Wheels orange body, dark charcoal running surface — high contrast so the cars read against it at
CAVE viewing distances — with the two as separate submeshes of one extruded ribbon.

The circuit is held up by a column on the axis of each turn, which is the one line inside a spiral
that no part of the spiral passes through, with arms out to the track. Arms are struck only from track
that is actually going round a tower — curvature is the test, not position, because a point can be out
at the tower's radius while belonging to the straight that passes it, and an arm from there would be a
chord across the spiral rather than a spoke of it. The high front straight gets plain posts, from
level track only: the height window keeps them off the loop climbing away above, and a surface-normal
test keeps them off the barrel roll, where a post would appear to hold the track up by a point that is
at that moment upside down. The outermost point of each turn gets a bracket to the wall it is nearly
touching, which is what makes the circuit read as bolted into the room rather than floating in it.

Nothing carries a collider. The cars ride the centre line and a crashed one is scenery for a second
and a half, so colliders would only be something for the room's own physics to trip over.
