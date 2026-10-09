# Porting the Gun's Controller Input to the MiddleVR Wand

**Area:** Gun / input / MiddleVR
**Files:** `Assets/_Project/Scripts/MiddleVRWand.cs`, `Scripts/MiddleVRKeyboard.cs`, `Scripts/Gun/MiddleVRWand*.cs`, `Scripts/Core/MiddleVRWandMatchRestart.cs`, `Scripts/Player/SimplePlayerMovement.cs`, `Prefabs/Gun/Gun_MiddleVR.prefab`
**Category:** Cross-platform input — Meta Quest (OpenXR) → MiddleVR wand (CAVE)
**Related:** [MiddleVR CAVE setup](MiddleVR_CAVE_Setup.md)

---

## TL;DR

The Quest gun reads input through Unity's Input System (`<XRController>` bindings). The CAVE
has no Unity-XR controller — MiddleVR exposes a single **wand** with indexed buttons. The port
was almost free because the gun's fire input was already behind an interface
(`IGunInputSource`), and the only real work was **re-reading the same intents from
`MVR.DeviceMgr` instead of `InputAction`s**. Everything else (aim, pooling, tracer, targets) is
identical and untouched.

Key facts learned:
- MiddleVR wand buttons: `MVR.DeviceMgr.IsWandButtonPressed(uint)` = *held*,
  `IsWandButtonToggled(uint)` = *rising edge this frame* (it only fires on press, not release).
- `MVR.DeviceMgr` is **null until the MVRManager kernel initialises** — always guard.
- Button **indices are config-defined** (the `.vrx`), not standardised — you must discover them.

---

## 1. Why the port was a drop-in

`GunController` depends only on this:

```csharp
public interface IGunInputSource { bool FireHeld { get; } }
```

and auto-finds any implementation on its GameObject (`GetComponent<IGunInputSource>()`). So the
CAVE build just needed a new implementation — no change to gun logic. The lesson: **abstract the
*intent* ("fire is held"), not the device.** The non-fire inputs (debug toggles, match restart)
were *not* behind an interface, so each needed a parallel MiddleVR component (below).

## 2. The MiddleVR wand API

| Intent | Unity Input System (Quest) | MiddleVR wand (CAVE) |
|---|---|---|
| Held (analog/digital) | `action.ReadValue<float>() >= press` | `MVR.DeviceMgr.IsWandButtonPressed(b)` |
| Pressed this frame | `action.WasPressedThisFrame()` | `IsWandButtonToggled(b) && IsWandButtonPressed(b)` |
| Trackpad / axis | stick bindings | `GetWandHorizontalAxisValue()` / `…Vertical…()` |

Notes:
- Button index is `uint`. The trigger is a **button** in MiddleVR (the `.vrx` maps the analog
  pull to a digital press), so fire uses `IsWandButtonPressed`, not an axis.
- `IsWandButtonToggled` is a state-change flag. We confirmed (via the probe) it only fires on the
  **press** edge here, but AND-ing with `IsWandButtonPressed` makes the rising-edge read robust
  regardless of that detail and ignores the release edge.

## 3. One helper centralises the semantics

`MiddleVRWand` (static) wraps the null-guard, the `uint` cast, and the rising-edge logic so the
three input components don't repeat them. It's also `#if UNITY_STANDALONE_WIN || UNITY_EDITOR`
guarded (MiddleVR is Windows-only) so Android builds still compile, returning `false` off-platform.

```csharp
MiddleVRWand.Held(button)            // ≈ ReadValue >= press
MiddleVRWand.PressedThisFrame(button) // ≈ WasPressedThisFrame()
MiddleVRWand.HorizontalAxis           // joystick, -1 left … +1 right (dead zone already applied)
MiddleVRWand.VerticalAxis             // joystick, -1 back … +1 forward
MiddleVRKeyboard.Held(Key.W)          // MiddleVR's DirectInput keyboard, named by Unity's Key enum
```

## 4. The components and the button map

| Action | Quest button | MiddleVR component | Wand button |
|---|---|---|---|
| **Fire** | right trigger | `MiddleVRWandInputSource` (on gun) | 0 |
| **Toggle gun model** | left X | `MiddleVRWandDebugToggles` (on gun) | 1 |
| **Toggle tracer line** | left Y | `MiddleVRWandDebugToggles` | 2 |
| **Toggle tracer dot** | right A | `MiddleVRWandDebugToggles` | 3 |
| **Restart match** | right B | `MiddleVRWandMatchRestart` (match object) | 4 |

All button indices are exposed as Inspector fields — they are **not** hard-coded, because the
mapping is per-`.vrx`. `Gun_MiddleVR.prefab` bakes in the gun-side components (fire + debug
toggles) plus `MVRAttachToNode (HandNode)` so it self-attaches to the tracked wand (see
[CAVE setup doc](MiddleVR_CAVE_Setup.md)). `MiddleVRWandMatchRestart` is the twin of
`AutoStartMatch` and lives on the match-manager object — it has no effect until the match flow
exists in `BootMiddleVR`.

## 4b. Player movement (`HolographicPlayer`) — the joystick and the keyboard problem

`SimplePlayerMovement` drives the little Player capsule from **three sources summed**: the wand
joystick (`VerticalAxis` → ±Z, `HorizontalAxis` → ±X), the keyboard as MiddleVR sees it, and the
keyboard as Unity sees it. Rise is **wand button 0** (trigger, Inspector field `m_RiseButton`) or
**Space**. Movement is world-axis — forward is +Z, the front screen — because you look *down on*
this player rather than stand inside it.

Two things learned bringing it up:

- **Unity's Input System keyboard is focus-bound.** `Keyboard.current` only sees keys while the
  Unity window (in the Editor, the Game view itself) has focus, and in the CAVE it seldom does —
  MiddleVR's always-on-top compositor is on the mosaic and clicks land there or on other Editor
  panels. MiddleVR's `vrDriverDirectInput` keyboard is the second road, the same one the kernel's
  own Shift+D / Shift+W shortcuts ride, so `MiddleVRKeyboard` wraps it and components OR the two.
- **A force just under static friction looks exactly like dead input.** The capsule sits on the
  ground at Unity's default friction 0.6, which needs μ·g ≈ 5.9 m/s² of push before it moves; the
  scene had `moveForce: 5`. The script now steers *velocity* (`m_MoveSpeed`, `m_Acceleration`,
  `m_RiseSpeed`) so friction is irrelevant and the speed is what the Inspector says.

## 5. Discovering the wand's button indices

Indices are config-defined, so you can't guess them. A temporary probe MonoBehaviour that logs
`IsWandButtonToggled` across indices `0..15` reveals them — drop it on any GameObject, press each
physical button, read the log:

```csharp
for (uint i = 0; i <= 15; i++)
    if (devices.IsWandButtonToggled(i))
        Debug.Log($"button {i} {(devices.IsWandButtonPressed(i) ? "PRESSED" : "released")}");
```

On the **VisBox wand** this surfaced active indices **0, 1, 2, 3, 4, 6, 7, 8, 11, 15** — i.e. far
more than the assumed 3, which is why buttons 3 and 4 worked all along (the early doubt was a
*visibility* problem, not an input one — see §6). Keep or re-add the probe whenever bringing up a
new wand/config.

## 6. Gotchas worth remembering

- **Logs go to the Unity Console / `Editor.log` in the editor**, and to
  `…/AppData/LocalLow/DefaultCompany/VR Demo/Player.log` in a build. "Nothing in the Console"
  was a **filter** issue (the info-message toggle was off) — the logs were in `Editor.log` the
  whole time. In a cluster, the **server** node holds the input log.
- **The tracer dot is a fixed-size world sphere** placed at the aim point; `Tracer` sets its
  position/rotation but not scale. At CAVE aim distances the default 3 cm was invisible — bumped
  to **0.15 m and defaulted on** in `Gun_MiddleVR.prefab`. (A constant-apparent-size option
  scaling by camera distance is a one-liner in `Tracer.Update` if needed.)
- **The tracer beam is a held laser sight, not a muzzle flash.** `Tracer.m_LineMode` defaults to
  `Always`: while the line is on it is redrawn from the muzzle to the aim point every frame, in
  `LateUpdate` so it uses the same frame's wand pose rather than trailing one behind. Set the mode to
  `OnShot` for the original brief-beam-per-shot behaviour, which is the only thing `m_LineLifetime`
  still applies to. Either way the line only shows when `m_LineEnabled` is on (wand button 2).
- **`Camera.main` may be null/untagged under MiddleVR** (it disables the template camera and
  builds its own). Code that caches `Camera.main` must null-guard — the dot's billboard rotation
  does; visibility is unaffected.
- **CAVE wands can have fewer buttons than two Quest controllers.** Five distinct actions may not
  fit a given wand; the trackpad axes are the overflow input. (This wand had plenty — see §5.)

## 7. Checklist — adding a new wand-button action

- [ ] Read the intent via `MiddleVRWand.Held` / `.PressedThisFrame` (never call `MVR.DeviceMgr`
      raw — keep the guard/edge logic in one place).
- [ ] Expose the button index as a `[SerializeField] int`, don't hard-code it.
- [ ] Run the probe to find the real index in the current `.vrx`.
- [ ] If it mirrors a Quest component, make a parallel MiddleVR component and swap it in the
      `_MiddleVR` prefab (keep the same component fileID so references survive).
- [ ] Guard MiddleVR API use with `#if UNITY_STANDALONE_WIN || UNITY_EDITOR`.
