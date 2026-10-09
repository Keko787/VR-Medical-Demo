# Running the CAVE on the RTX A6000 (higher frame rate, no wand lag)

**Area:** MiddleVR / CAVE rendering / GPU placement
**Applies to:** Unity **2022.3.9f1 LTS**, MiddleVR **2.4.0.4**, the VisBox CAVE host **VISCUBE** (Windows 10)
**Category:** performance — 14 FPS vs 52 FPS depending on which GPU Unity starts on
**Background:** `Docs/VISCUBE Wand Lag.html` (the full investigation)

---

## TL;DR

VISCUBE has two GPUs. The **Quadro RTX 4000** drives the console monitor (the Windows
*primary* display). The **RTX A6000** drives the 2560×6400 CAVE mosaic. Unity always
creates its graphics device on the primary display's GPU, and MiddleVR's compositor
follows Unity's GPU — so anything started the normal way renders the CAVE on the RTX 4000
and Windows copies every stereo frame across to the A6000. That path runs at **~14 FPS
(72 ms/frame)** and is what the wand lag was.

Rendering on the A6000 instead gives **~52 FPS (19 ms/frame)** with identical content.
Measured 2026-09-14 with the CAVEWeather build, same exe and config, only the GPU swapped.

There are two ways to get there:

| How you run it | What to do | Verified |
|---|---|---|
| **Standalone build** launched with `--config` | Nothing — MiddleVR's player wrapper relaunches the game on the right GPU automatically | yes (Weather build, 52.6 FPS) |
| **Unity Editor** Play mode | Start `Unity.exe` with **`-force-device-index 1`** | renderer confirmed A6000; FPS still to be read from a Play run |

Everything below is the detail.

---

## 1. Standalone builds — launch through the wrapper

MiddleVR's post-build step replaces the built `.exe` with `MiddleVR_UnityPlayer.exe`
(a launcher stub). When you start it with `--config <file.vrx>`, the stub reads the config,
finds the DXGI adapter whose output contains the CAVE window, and relaunches the real
player on it with `-gpu 1 -force-device-index 1 --start-unity-process`. You get the A6000
for free — **as long as you launch with `--config`**.

### Steps

1. **Rebuild the project** (File ▸ Build Settings ▸ *Build*, not *Build & Run*):
   - Windows, x86_64, Mono.
   - Tick the scenes you want (`ShootingGallery MiddleVR`, `Physics MiddleVR`, …).
   - The current `Build\` folder is stale (June 2026, `Boot.unity` only, OpenXR still
     pre-initialised). Overwrite it.
2. **Make a launcher `.bat`** next to the exe (mirrors `RUN_CAVEWeather_HDRP.bat`):

   ```bat
   "C:\Users\Exx\KostageProjects\VR-Demo\Build\VR Demo.exe" --config "C:\Users\Exx\Desktop\visbox\config\MiddleVR\MVR.vrx" -popupwindow -force-d3d11
   ```

   Do **not** add `--start-unity-process` — that is the flag the stub gives its child, and
   passing it yourself skips the relaunch and pins the game to the RTX 4000 (14 FPS).
   `--mvr-window-geometry` from the old Weather `.bat` is a MiddleVR 1.x flag; 2.4 ignores it.
3. **Optional but recommended — match the quality level.** Standalone default quality is
   level 5 "Ultra" (`Quality URP Config`: HDR on, per-pixel additional lights, 10 m shadow
   distance) whereas the Editor plays at level 1 "Low" (`Performance URP Config`). Set
   *Project Settings ▸ Quality ▸ Default (Windows)* to the same level the Editor uses so a
   build isn't secretly heavier than what you tested.
4. **Run the `.bat`** (or register the exe in MiddleVR Config and run it from there).

### Verify

Open `%LOCALAPPDATA%\Temp\MiddleVR\MiddleVR_<today>\`:

- `MiddleVR_VR Demo.txt` — the kernel log. Look for
  `Using Adapter 'NVIDIA RTX A6000': LUID=0:2618f` and the `Frame: N, FPS: x` lines
  (one every 60 frames).
- `Unity_Player.txt` — the relaunched player's Unity log. `Renderer: NVIDIA RTX A6000`.

In Task Manager ▸ Details there should be **two** `VR Demo.exe` processes (stub + child);
the child's 3D load sits on **GPU 1** (the A6000).

---

## 2. Unity Editor — add `-force-device-index 1`

The Editor is never relaunched by MiddleVR, so it always starts on the primary display's
GPU. Unity's docs list `-force-device-index` as macOS-only for the Editor, but the Windows
2022.3 Editor honours it (verified 2026-09-14: `Editor.log` → `Renderer: NVIDIA RTX A6000`).

The index is the **DXGI adapter order**, which on VISCUBE is:

```
0  NVIDIA Quadro RTX 4000   LUID 0:22671   (console monitor — the default)
1  NVIDIA RTX A6000         LUID 0:2618f   (CAVE mosaic)
2  NVIDIA Quadro RTX 4000   LUID 0:2cde3   (second enumeration, likely the Parsec virtual display)
3  Microsoft Basic Render Driver
```

(The compositor prints this list at start-up under `Found adapter …` in the kernel log.)

### Option A — Unity Hub (persistent)

1. Unity Hub ▸ *Projects* ▸ the ⋮ menu on **VR-Demo** (and **Sit-Rep-CAVE**) ▸
   **Add command line arguments**.
2. Enter: `-force-device-index 1`
3. Open the project from the Hub as usual.

### Option B — launch directly (one-off)

```bat
"C:\Users\Exx\Documents\Unity Projects\2022.3.9f1\Unity 2022.3.9f1\Editor\Unity.exe" -force-device-index 1 -projectPath "C:\Users\Exx\KostageProjects\VR-Demo"
```

### Option C — no flag: make the mosaic the main display

Windows Settings ▸ System ▸ Display ▸ select the 2560×6400 mosaic ▸ *Make this my main
display*, then start Unity. Unity's default adapter is now the A6000. Undo it afterwards
or the desktop lives on the CAVE walls.

### Verify

1. `%LOCALAPPDATA%\Unity\Editor\Editor.log`, near the top:
   `GfxDevice: creating device client` followed by `Renderer: NVIDIA RTX A6000 (ID=0x2230)`.
   If it says `Quadro RTX 4000 (ID=0x1eb1)` the flag didn't take.
2. Press Play with the CAVE scene open. In
   `%LOCALAPPDATA%\Temp\MiddleVR\MiddleVR_<today>\MiddleVR_Unity.txt` check
   `Using Adapter 'NVIDIA RTX A6000'` and the `Frame: N, FPS:` lines. Sep-11 sessions on
   the RTX 4000 logged 12–16 FPS; expect the Weather build's range (~50) or better.

Keep the **Game** tab in front while playing — MiddleVR presents from a
`WaitForEndOfFrame` coroutine, which the Editor only fires while the Game view repaints.

---

## 3. Reading the frame rate

The Shift+D FPS overlay does not work on Unity 2022.3 (`MVRManagerScript.Start` throws on
`Arial.ttf`), so use the kernel log:

```
[ ] Frame: 60,   FPS: 56.0948, Delta Time: 0.0183013     ← A6000
[ ] Frame: 60,   FPS: 13.8598, Delta Time: 0.0697487     ← RTX 4000
```

`Delta Time` is the last frame's period in seconds; `FPS` is the rolling average.
Log folders under `%LOCALAPPDATA%\Temp\MiddleVR\` are purged after about a week, so copy
anything you want to keep.

---

## 4. After the GPU is right: cheap latency wins

Motion-to-photon is roughly two to three frame periods, so once the period is ~19 ms these
are worth doing; at 72 ms they were noise under the main problem.

- `QualitySettings.maxQueuedFrames = 1` at start-up — removes one queued frame of pose age.
- `Tracer.cs`: move the aim-dot placement from `Update` into `LateUpdate` next to the beam
  draw (both read `GunController.AimPoint`; both scripts are execution order 0).
- Sit-Rep: port VR-Demo's `LateUpdate` beam draw (its `Tracer` still draws in `Update`),
  and give `WandOperatorInput` an execution order after MiddleVR's −100 instead of −1000.
- Trim the 8-view render (shadow-map size, shadow distance, no HDR) to head toward the
  mosaic's 120 Hz.

---

## 5. Why this was hard to see

- Every CAVE session of VR-Demo and Sit-Rep had been an **Editor** session; no current
  build had ever been run in the CAVE, so "project vs project" was really "Editor vs
  wrapped build".
- The lagging frame rate was scene-independent (a 126-rigidbody physics scene and an
  empty gallery both at 14 FPS), which rules out content but doesn't point at the GPU
  until you read the compositor's `--adapter` argument.
- The Weather build's `Player.log` in `LocalLow` shows `Renderer: NVIDIA Quadro RTX 4000`
  — but that log is from the *stub* (or a no-config double-click), not the relaunched
  child, whose log goes to the MiddleVR temp folder instead.
- The VisBox install notes (`Desktop\visbox\doc\NOTES-INSTALL-WIN10.txt`, ~line 124)
  already said "`-adapter 2` required (console monitor on 0, sound on 1)" — written for
  MiddleVR 1.7, where the flag was different, and easy to miss.

Note for `MiddleVR_CAVE_Setup.md`: on VISCUBE, pressing Play **does** light the physical
walls (the "preview window" is the compositor on the mosaic), and the `Arial.ttf` exception
fires whether or not *Show FPS* is on — neither affects rendering.
