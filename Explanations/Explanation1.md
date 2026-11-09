# OakHaven — Deep Dive #1: The Day/Night Cycle & The Equipment System

> This document explains, in full detail, the two systems we designed and built together:
> the **day/night cycle** (`DayNightCycle`) and the **held-item equipment system**
> (`ItemDefinition` extension + `PlayerEquipment` + `EquipmentSetup`).
> It covers the architecture, the workflow, what every piece of code does, *why* it exists,
> and the history of every bug we hit on the way — because the "why" of each line is
> usually a bug that already happened once.
>
> **Files covered:**
>
> | File | Role |
> |---|---|
> | `Assets/Scripts/DayNight/DayNightCycle.cs` | The day/night cycle (runtime) |
> | `Assets/Scenes/DayTIme.unity` | Scene data it reads: curves, lights, volumes |
> | `Assets/Scripts/Items/ItemDefinition.cs` | Item data (+ the new `equippedPrefab` field) |
> | `Assets/Scripts/Equipment/PlayerEquipment.cs` | Equip/unequip + held visuals (runtime) |
> | `Assets/Scripts/Editor/EquipmentSetup.cs` | `Tools > OakHaven > Setup Equipment` (editor) |
> | `Assets/Scripts/Editor/NetworkingSetup.cs` | Path-resolution fix (editor) |
> | `Assets/Prefabs/Items/stone_axe.prefab` | The held model + the pitfalls of authoring it |
>
> Written 2026-10-17.

---

# PART 1 — THE DAY/NIGHT CYCLE

## 1.1 The problem it solves

We want time to flow in the game world (a full 24-hour in-game day), and the *look* of the
world to follow that time continuously:

- The sun rises, travels, sets.
- The moon fades in at dusk, out at dawn.
- Fog color/density, ambient light, skybox tint and exposure all drift from a "day look"
  to a "night look".
- A global post-processing Volume ("NightTimeProfile") fades in at night.
- The game is **networked** — so all connected clients must agree on what time it is,
  without any client being able to cheat or desync.

The core difficulty is not "make things change" — it's making them change **smoothly,
identically on every machine, and safely on a client/server architecture**.

## 1.2 Architecture at a glance

```
                     ┌──────────────────────────────────────────────┐
                     │                 SERVER (host)                │
                     │  _hour advances in Update()                  │
                     │  every 1 second: _serverHour.Value = _hour    │
                     │        │                                      │
                     │        └── NetworkVariable<float> (replicated)│
                     └───────────────────────┬──────────────────────┘
                                             │ NGO delta messages
                     ┌───────────────────────▼──────────────────────┐
                     │              EVERY CLIENT                    │
                     │  OnServerHourChanged(v)                      │
                     │   small diff → smooth correction (no pop)    │
                     │   big diff  → one-time hard snap             │
                     │  Update(): local clock keeps ticking         │
                     └───────────────────────┬──────────────────────┘
                                             │
                     ┌───────────────────────▼──────────────────────┐
                     │  Apply() — runs EVERY frame on EVERY peer     │
                     │  reads _hour → evaluates curves → writes      │
                     │  sun rotation/color/intensity, moon, fog,    │
                     │  ambient, skybox, volume weight, held lights  │
                     └──────────────────────────────────────────────┘
```

**Key design decision — time is server-authoritative.** The server owns the clock and
replicates it. Clients keep a *local* ticking clock for smoothness between syncs, but they
never *decide* what time it is; they only reconcile toward the server's value. This matches
the project-wide rule from `AGENTS.md`: state changes replicate via `NetworkVariable`
(ServerWrite / EveryoneRead), clients never mutate authoritative state.

**Second key decision — visuals are computed locally, every frame, on every peer.** The
server does not send fog colors or sun angles over the network. It only sends the *hour*
(one float). Every machine then computes the visuals from that hour using local data
(curves, colors). This is the "data-driven, id-over-asset" pattern the whole project uses:
send the small fact, resolve the big object locally.

## 1.3 The fields, one by one

```csharp
[SerializeField] private float dayLengthSeconds = 600f;
```
Real-world seconds per full in-game 24h. Default 600 (10 real minutes per day). **The
scene currently overrides this to 60** — one real minute per day, which we used for
testing. At 60, dusk alone lasts ~5.5 real seconds — that's why everything feels fast in
play mode even though it's continuous. Raise it when you want the real pace.

```csharp
[SerializeField, Range(0, 24)] private float startHour = 8f;
```
The hour the server starts the world at on boot. Only the *server* uses this — clients
receive the real time via sync. 8 = morning.

```csharp
[SerializeField] private Light sun;        // the directional day light
[SerializeField] private Light moon;       // the directional night light
[SerializeField] private Volume nightVolume;   // global Volume w/ NightTimeProfile
[SerializeField] private GameObject[] nightOnlyObjects; // meshes toggled with night
[SerializeField] private Light[] nightLights;      // lights FADED with night
```
All scene references assigned in the Inspector. `nightOnlyObjects` are things like the
diner sign meshes — they get `SetActive(true/false)` at the day/night boundary.
`nightLights` are actual `Light` components (diner lamps) — instead of popping on/off,
their intensity is faded continuously.

```csharp
[SerializeField] private AnimationCurve nightAmount;   // 0 = day, 1 = night
[SerializeField] private AnimationCurve sunIntensity;  // sun brightness over the day
[SerializeField] private Gradient sunColor;            // sun color over the day
[SerializeField] private float moonMaxIntensity = 0.9f;
```
**The two curves are the heart of the system.** `nightAmount` is a 0→1 "how night is it?"
value sampled by time-of-day (x = hour/24). Everything else in `Apply()` is lerped by this
single value. `sunIntensity` independently shapes the sun's brightness (so sun fade-out and
night ramp-in can be choreographed to not overlap — more in §1.6).

```csharp
// day/night pairs for fog, ambient, skybox:
dayFogColor / nightFogColor, dayFogDensity / nightFogDensity
dayAmbient / nightAmbient
daySkyTint / nightSkyTint, daySkyExposure / nightSkyExposure
moonColor
```
Every visual property is expressed as a **day value and a night value** with the curve
deciding the blend. This is what makes the system tunable entirely from the Inspector —
no code changes to restyle the world.

```csharp
private const float MainLightSwapThreshold = 0.05f;
private const float HardSyncThreshold = 1f;
private const float CorrectionRate = 3f;
```
- `MainLightSwapThreshold` — the night-amount at which we swap which light is
  `RenderSettings.sun`. Kept at 0.05 because at that moment the sun's intensity is already
  0 and the moon is barely lit — the swap is invisible (see §1.5, the "invisible swap").
- `HardSyncThreshold` — clock corrections bigger than 1 hour snap instantly; smaller ones
  are absorbed smoothly (see §1.4).
- `CorrectionRate` — how fast a pending clock correction is absorbed (fraction per
  second). 3 → corrections dissolve in roughly a third of a second.

```csharp
private readonly NetworkVariable<float> _serverHour = new NetworkVariable<float>();
```
The replicated clock. One float. Default permissions are exactly what we want
(Server write, Everyone read).

```csharp
private float _hour;            // this peer's current local clock (0..24)
private float _hourCorrection;  // pending smooth correction toward server time
private float _syncTimer;       // server: 1-second sync cadence
private Material _sky;          // runtime clone of the skybox material
private float[] _nightLightBase;// original intensities of nightLights
private bool _wasNight;        // edge-detection state for nightOnlyObjects
private bool _moonPhase;       // edge-detection state for the sun/moon swap
```

## 1.4 Networking: the clock, without the snapping

### The naive version (and why it snapped)

The original code did:

```csharp
_serverHour.OnValueChanged += (_, v) => _hour = v;   // hard overwrite
```

The server publishes once per second. The client also advances its own local clock every
frame. When the delta message arrives, it carries a value that is already
*(network latency + send-staleness)* old. Hard-setting means the client's clock is yanked
**backwards by the latency** once per second — and since every visual (sun angle, fog,
colors) is a function of the clock, the whole world visibly *pops* once every second on
every non-host client. That was snapping bug #3 from the original build.

### The fixed version

**Client side — subscribe, then reconcile:**

```csharp
public override void OnNetworkSpawn()
{
    if (IsServer)
    {
        _hour = startHour;            // server owns the initial time
        _serverHour.Value = _hour;    // publish immediately for late joiners
    }
    else
    {
        if (_serverHour.Value > 0f) _hour = _serverHour.Value;
        // (0 = not synced yet — better to sit at our default than flash midnight)
        _serverHour.OnValueChanged += OnServerHourChanged;
    }
}
```

```csharp
private void OnServerHourChanged(float _, float serverHour)
{
    if (IsServer) return;

    // Shortest path around the 24h wrap: 23.9 → 0.1 is 0.2h forward, not 23.8h backward.
    float diff = serverHour - _hour;
    if (diff > 12f) diff -= 24f;
    else if (diff < -12f) diff += 24f;

    if (Mathf.Abs(diff) > HardSyncThreshold)
    {
        _hour = serverHour;      // fresh join / real desync: snap once (invisible at spawn)
        _hourCorrection = 0f;
    }
    else
    {
        _hourCorrection = diff;  // normal drift: absorb smoothly
    }
}
```

Why the two branches? A fresh client joining at 18:00 while thinking it's 0:00 is *6 hours
behind* — you don't want the sun to rewind through the whole afternoon over a third of a
second; you snap once during the spawn fade-in. But a normal sync arriving 80 ms late is a
tiny error — absorbing it over ~0.3 s is imperceptible, while hard-setting it would be a
per-second visible pop.

**Every frame, on every peer (`Update`):**

```csharp
float advance = 24f * Time.deltaTime / dayLengthSeconds;         // natural ticking
float absorb  = _hourCorrection * Mathf.Clamp01(Time.deltaTime * CorrectionRate);
_hourCorrection -= absorb;                                       // correction decays
_hour = Mathf.Repeat(_hour + advance + absorb, 24f);              // wrap-safe clock

if (IsServer)   // server re-publishes on a 1-second cadence
{
    _syncTimer += Time.deltaTime;
    if (_syncTimer >= 1f) { _syncTimer = 0f; _serverHour.Value = _hour; }
}
Apply();
```

- `advance` — the clock ticks locally so visuals are smooth at frame rate, even between
  syncs.
- `absorb` — a fraction of the pending correction is applied each frame
  (`Clamp01(dt * 3)` ≈ exponential decay, time-constant ~⅓ s). The correction melts into
  the clock instead of stomping it.
- `Mathf.Repeat(..., 24f)` — wraps the clock into [0, 24). We use `Mathf.Repeat` and not
  C#'s `%` because `%` can return negative values (e.g. `-1f % 24f == -1f`), which would
  push the hour out of range and break the curve sampling.
- The server never has a correction (it *is* the truth) but runs the same math harmlessly.

## 1.5 `Apply()` — the visual engine, line by line

Runs every frame on every peer. Everything is a pure function of `_hour`.

```csharp
if (_sky == null) return;
```
Guard added after a real bug class we hit: **`Update` runs on NetworkBehaviours *before*
network spawn.** If `OnNetworkSpawn` had created `_sky` and Relay failed to connect, every
frame would throw a NullReferenceException. (In the final code, `_sky` is created in
`Awake` so this guard is a belt-and-suspenders backstop for "no skybox assigned at all".)

```csharp
float t = _hour / 24f;                                  // 0..1 time of day
float night = Mathf.Clamp01(nightAmount.Evaluate(t));   // 0..1 nightness
```
`nightAmount.Evaluate(t)` samples the curve. The `Clamp01` exists because of a real
corrupted-curve bug (see §1.6): a curve can evaluate *outside* 0..1 if its key tangents
are wrong, and `Color.Lerp(a, b, t)` **extrapolates** for t outside [0,1] — fog, ambient,
and sky colors would swing past their intended endpoints. Clamping makes a bad curve
produce an ugly-but-safe value instead of a broken one.

```csharp
sun.transform.rotation = Quaternion.Euler(t * 360f - 90f, 30f, 0f);
```
The sun's travel. Worked example with the `-90` offset:
- `t = 0.25` (6:00) → pitch = 0° → sun at the horizon (sunrise)
- `t = 0.50` (noon) → pitch = 90° → sun straight overhead
- `t = 0.75` (18:00) → pitch = 180° → horizon again (sunset)
- `t = 0/1` (midnight) → pitch = -90° → straight under the world

The fixed `30f` yaw gives the arc a slight side angle so shadows aren't perfectly
north-south all day (looks less mechanical).

```csharp
sun.color = sunColor.Evaluate(t);        // gradient: orange at horizon, cream at noon
sun.intensity = sunIntensity.Evaluate(t);// 0 before dawn, ramps up, peaks midday, back to 0
moon.color = moonColor;                  // cold blue-ish
moon.intensity = night * moonMaxIntensity; // moon fades IN as night rises
```
Sun and moon are **crossfaded through their intensities** — never switched. Notice the
sun's brightness is *independent* of `night`: it follows its own curve so we can
choreograph exactly when the sun dies vs when the moon rises (that choreography is the
dead-zone fix, §1.6).

```csharp
nightVolume.weight = night;   // NightTimeProfile fades in with nightness
```
A `Volume`'s `weight` is a 0..1 blend factor — the entire night post-process stack
(tonemapping, color adjustments, bloom, white balance, film grain, volumetric fog) fades
in continuously. No popping because weight is continuous.

```csharp
RenderSettings.fogColor   = Color.Lerp(dayFogColor,   nightFogColor,   night);
RenderSettings.fogDensity = Mathf.Lerp(dayFogDensity, nightFogDensity, night);
RenderSettings.ambientLight = Color.Lerp(dayAmbient,  nightAmbient,    night);
```
Fog and ambient are global scene settings (`RenderSettings`) — the script writes them
every frame, which is exactly why the **Environment tab of the Lighting window cannot be
used to change these at runtime**: this code would stomp any manual edit within one
frame. Day/night look values live on the *component*, not the Lighting tab (full
ownership table in §1.8).

```csharp
_sky.SetColor("_Tint", Color.Lerp(daySkyTint, nightSkyTint, night));
_sky.SetFloat("_Exposure", Mathf.Lerp(daySkyExposure, nightSkyExposure, night));
```
`_sky` is a runtime **clone** of the scene skybox material (cloned in `Awake`, see below).
The skybox is `Skybox/Panoramic` (the Cloudy01 asset), which exposes `_Tint` and
`_Exposure` — so we fade the sky darker and bluer at night. `_Rotation`/texture come from
the source material; only these two animated properties are written.

```csharp
for (int i = 0; i < nightLights.Length; i++)
    if (nightLights[i] != null)
        nightLights[i].intensity = _nightLightBase[i] * night;
```
Diner lights fade in/out proportionally to nightness — the replacement for the original
`SetActive(true)` pop. `_nightLightBase` captured each light's authored intensity in
`Awake`; we multiply it by nightness so a light authored at 3.0 fades 0→3.0.

```csharp
bool moonPhase = night > MainLightSwapThreshold;   // 0.05
if (moonPhase != _moonPhase)
{
    _moonPhase = moonPhase;
    if (sun != null && moon != null)
        RenderSettings.sun = moonPhase ? moon : sun;
}
```
**The invisible swap.** `RenderSettings.sun` nominates which directional light is "the
sun" (drives shadow-light selection, some shaders, third-party volumetric fog). You
cannot lerp a reference — it's one light or the other — so the *timing* must be chosen so
the swap is invisible. We swap at `night = 0.05`:

- At dusk that moment is `t ≈ 0.78` — the sun's intensity is already **0** (its curve
  hits 0 at 0.77) and the moon is at a whisper (0.05 × 0.9). Swapping two invisible
  lights: no pop.
- At dawn (`night` falls below 0.05 around `t ≈ 0.226`) the sun's intensity is still 0
  (its curve only starts rising at 0.23). Again invisible.

The edge-trigger (`moonPhase != _moonPhase`) means this runs exactly twice per day, not
every frame. The original code did this swap at `night == 0.5` — with the sun still
bright and the moon at half power — that was snapping bug #2.

```csharp
if (IsNight != _wasNight)
{
    _wasNight = IsNight;
    foreach (var go in nightOnlyObjects) if (go != null) go.SetActive(IsNight);
}
```
Pure mesh objects (signs, glowing props) can't fade via a `Light` — they toggle. This is
edge-triggered at `night > 0.5` (gameplay "is it night"), so it fires exactly twice a
day. By that moment the scene is already deep into the night look, so a mesh appearing
is far less noticeable than it was in the original version (where it popped at the same
instant as the hard light swap).

## 1.6 `Awake()` and the scene data — where all the original bugs lived

```csharp
private void Awake()
{
    if (RenderSettings.skybox != null)
    {
        _sky = new Material(RenderSettings.skybox);   // clone!
        RenderSettings.skybox = _sky;
    }
    RenderSettings.ambientMode = AmbientMode.Flat;
    // capture nightLights base intensities into _nightLightBase
}
```

Three decisions:

1. **Clone the skybox.** Editing `RenderSettings.skybox` directly would modify the
   *shared material asset* (`Cloudy01.mat`) — every scene using it, and the asset on
   disk, would get our runtime values. Cloning gives us a private copy to animate.
2. **In `Awake`, not `OnNetworkSpawn`.** If Relay fails and the object never spawns,
   `Update` still runs — with `_sky` null, that was a guaranteed per-frame
   NullReferenceException (and because the exception aborted `Apply()` mid-way, the
   `IsNight` block silently never ran). Building visuals state in `Awake` makes the
   lighting work even with networking dead.
3. **Force `AmbientMode.Flat`** so our single flat `ambientLight` color is what's
   actually used (the scene was set to Gradient ambient, which would have fought us).

### The curve data in the scene (and why two keys were changed by hand)

`nightAmount` keys in `DayTIme.unity`:

| time (x) | hour | value | meaning |
|---|---|---|---|
| 0.00 | 0:00 | 1 | deep night |
| 0.22 | 5:17 | 1 | night holds until here |
| 0.30 | 7:12 | 0 | dawn ramp finishes |
| 0.70 | 16:48 | 0 | day holds until here |
| **0.77** | **18:29** | **0** | dusk ramp *starts here* (moved from 0.80) |
| 1.00 | 24:00 | 1 | full night |

`sunIntensity` keys: `0.23→0`, `0.30→0.6`, `0.50→1.2`, `0.70→0.6`, `0.77→0`.

Two scene-level bugs were fixed here:

**Bug A — corrupted tangents (the "dusk rollercoaster").** The dusk segment (0.8→1.0,
rising 0→1) had **negative slopes** stored on its keys (`-7.37`, `-7.0`). Unity's runtime
curve evaluation is Hermite interpolation driven by each key's `inSlope/outSlope` — the
sign was backwards, so the evaluated `night` value **dipped below 0** at the start of
dusk and **overshot past 1** near midnight. Downstream: negative moon intensity,
out-of-range volume weight, and `Color.Lerp` extrapolating fog/ambient/sky colors outside
their day↔night endpoints. Fix: both keys' slopes set to `0` (flat tangents → smooth
S-ramp, same as the dawn segment which was already correct).

**Bug B — the dead zone (3% of the day with zero directional light).** The sun's
intensity hits 0 at `t=0.77`, but `nightAmount` originally stayed 0 until `t=0.80` —
so from 18:29 to 19:12 *both* lights were off, then the moon faded in. That read as
"dusk → sudden black → pop". Fix: move the `nightAmount` dusk key from 0.80 to **0.77**
so the moon starts rising at the exact frame the sun dies. Crossfade, no gap.

**Moral of both:** a curve is data, but it is still *code that runs* — tangent signs and
key alignment are behavior. This is also why `Apply()` now `Clamp01`s the evaluation.

## 1.7 What is owned by the script vs. editable in the Inspector

| Where | What | Editable at runtime? |
|---|---|---|
| **DayNightCycle component** | all day/night fog/ambient/sky values, curves, `dayLengthSeconds`, `startHour` | ✅ yes — these ARE the knobs |
| **NightTimeProfile** (Volume asset) | every post-processing override (tonemap, colors, bloom, grain, fog) | ✅ yes — script only animates the Volume's *weight* |
| **Sun/Moon Light components** | shadow settings (type, strength, bias, resolution) | ✅ yes — script never touches them |
| **Lighting tab → Environment** | ambient color/mode, fog color/density, skybox tint/exposure | ❌ no — `Apply()` stomps these every frame |
| **Sun/Moon Light components** | rotation, color, intensity | ❌ no — driven by curves |
| **Any light in `nightLights`** | intensity | ❌ no — faded by script from its captured base |
| **Lighting tab → rest** | baked GI, reflections, shadow color | ✅ yes — untouched |

## 1.8 Known behavior notes

- **In-scene DayNightCycle needs no ownership**: every peer runs `Apply()`, but only the
  server writes `_serverHour`. The visual writes (`RenderSettings`, materials) are
  process-local, so there's no conflict.
- **Curve shape is deliberately "flat-then-fast"**: full day look from ~7:12 to ~16:48.
  If you want the lighting drifting all day long, widen the ramps in the curve editor —
  the keys are safely flat-tangent now, so dragging them can't reintroduce Bug A.
- **Volume weight & fog**: `NightTimeProfile`'s VolumetricFog has main-light
  contribution disabled, so the sun/moon swap matters for *shadows* (and any
  RenderSettings.sun-driven asset), not for that fog.

---

# PART 2 — THE EQUIPMENT SYSTEM (held stone axe)

## 2.1 The goal

Craft a stone axe at a workbench → press **Q** → the axe appears in the player's right
hand, on **every** player's screen → press **Q** again → it's gone. No cheating: a client
can request, only the server decides.

## 2.2 Why we did NOT use Animation Rigging

- The package (`com.unity.animation.rigging`) isn't even installed.
- It's the wrong tool: Animation Rigging is for **procedural posing** (two-bone IK making
  a hand *reach* a grip point, look-at chains). Holding an axe is an **attachment**
  problem, not a posing problem.
- The standard solution: a **hand socket** — an empty GameObject parented to the hand
  bone — with the item's visual prefab parented to it. The Animator animates the hand the
  same way on every client (deterministic), so the socketed item follows the hand for
  free, with **zero network traffic** for the visual.

Animation Rigging remains a *future* option (only if the static hand pose on the handle
looks bad and we want IK to close the fist around the grip).

## 2.3 Phase 0 — the model, and THE golden rule

The axe FBX (modeled in Blender, exported to `Blender Assets/Exported/`, imported to
`Assets/ImportedAssets/OriginalAssets/`, prefab at `Assets/Prefabs/Items/stone_axe.prefab`).

**The golden rule of held-item prefabs:**

> The prefab's root must sit at **(0,0,0)** with its **pivot at the grip**.

Why: `PlayerEquipment` attaches the item with
`Instantiate(prefab, handSocket, worldPositionStays: false)` — the prefab's root local
transform is applied **relative to the socket**. If the root's position isn't zero, the
axe doesn't sit on the hand — it sits at *hand + whatever-the-root-offset-is*.

We learned this rule twice, the hard way:

**Bug C — the axe 700 meters away.** The prefab was created by dragging a model that was
standing at world **(409.97, 0, 593.98)** in the level; Unity baked that world position
into the prefab root's *local* position. At runtime the axe instantiated at
`hand + (410, 0, 594)` — equipped, rendered, logged... and invisible, floating out in
the world. Fix: prefab root position zeroed. Symptom to remember: *"log says equipped,
nothing in hand, no errors"* → the object exists somewhere, check where.

**Bug D — world pasted into a local slot.** During tuning, the socket's *local* position
was overwritten with **world** coordinates copied from Play mode (−535, 330, 5). The
socket is a **child of the hand bone** — its Inspector fields are relative to the *bone*,
not the world. The socket (and the axe under it) teleported ~630 m from the hand.
Fix: socket local position back to (0,0,0); the correct way to transfer Play-mode tuning
is `Copy Component` on the socket's Transform (copies *local* values), then
`Paste Component Values` on the prefab socket after stopping Play.

> **Rule of thumb:** a socket's/attached item's local position should always be (0,0,0)
> (or tiny values like ±0.05). Numbers in the hundreds = wrong space was pasted.

## 2.4 Phase 1 — the data: `ItemDefinition.equippedPrefab`

```csharp
[Header("Equippable")]
[Tooltip("Visual prefab attached to the hand socket when equipped. Null = not a held item.")]
public GameObject equippedPrefab;
```

One field. That's the whole phase. Everything else in the project already works this
way — items are data (ScriptableObjects), identified over the network by **string id**
(`itemId`, ≤ 32 chars because it rides in `FixedString32Bytes`), resolved through the
static `ItemDatabase`. We extend the *data* with "what does holding this look like",
and keep the *network contract* untouched: **the prefab never crosses the network** —
every client already has it (or will via the build); only the id does.

Assignments in this project: `ItemDef_StoneAxe.equippedPrefab → stone_axe.prefab`
(done by the editor script, by `itemId` match — never by filename).

## 2.5 Phase 2 — `PlayerEquipment`, line by line

The component lives on the player prefab root (same GameObject as `PlayerController`,
`Inventory`, etc.). It has exactly two serialized fields:

```csharp
[SerializeField] private Transform handSocket;   // child of Ch18's right-hand bone
[SerializeField] private KeyCode equipKey = KeyCode.Q;  // E is taken by workstations
```

### The networked state

```csharp
private readonly NetworkVariable<FixedString32Bytes> _equippedItemId =
    new NetworkVariable<FixedString32Bytes>(
        default,                                   // "" = nothing equipped
        NetworkVariableReadPermission.Everyone,    // other players must SEE the axe
        NetworkVariableWritePermission.Server);    // only the server decides
```

- **`FixedString32Bytes`, not `string`** — `NetworkVariable` can't replicate managed
  types; this matches `InventoryItem.ItemId` exactly (same using, same 32-char budget).
- **Everyone-read** because the visual must appear on *all* clients, and in the future
  gameplay (tree chopping) will ask "what does that player hold?".
- **Server-write** — even though it's "just cosmetics", it's game state, and the project
  rule is: state changes go through the server.

```csharp
private GameObject _heldInstance;   // local visual only — NEVER a NetworkObject
private Inventory _equipment;       // the equipment inventory (tools), NOT ResourceInventory
```

`_heldInstance` is the single most important field conceptually: the visible axe is a
**plain local GameObject** on each client. It is never spawned by NGO, has no
`NetworkObject`, no sync. (Why this is correct: §2.6.)

### Lifecycle

```csharp
public override void OnNetworkSpawn()
{
    base.OnNetworkSpawn();
    _equipment = GetComponent<Inventory>();
    _equippedItemId.OnValueChanged += OnEquippedChanged;
    UpdateHeldVisual();     // ← critical
}
```

- **`OnValueChanged` is subscribed on ALL peers** — not gated behind `IsOwner`. Visuals
  are everyone's business; only *input* is the owner's business (same split as
  `PlayerGathering`/`PlayerCrafting`).
- **`UpdateHeldVisual()` also runs once here.** `OnValueChanged` only fires on
  *changes*. If player A equips an axe and player B's client joins (or respawns)
  afterwards, B never receives an "it changed" event — without this line B would never
  see the axe. Applying the *current* value once at spawn covers every late joiner.

```csharp
public override void OnNetworkDespawn()
{
    _equippedItemId.OnValueChanged -= OnEquippedChanged;   // no leaked listeners
    if (_heldInstance != null) { Destroy(_heldInstance); _heldInstance = null; }
}
```

### Input — the owner's business only

```csharp
private void Update()
{
    if (!IsSpawned || !IsOwner) return;
    if (!Input.GetKeyDown(equipKey)) return;

    if (_equippedItemId.Value.IsEmpty)          // holding nothing → try to equip
    {
        string candidate = FindFirstEquippableItemId();
        if (candidate != null) EquipServerRpc(candidate);
        else Debug.Log("[Equipment] Nothing equippable in the equipment inventory.");
    }
    else                                        // holding something → unequip
    {
        UnequipServerRpc();
    }
}
```

- **`IsSpawned` in the guard** — same hazard as DayNightCycle: `Update` runs *before*
  network spawn, and pre-spawn `IsOwner` can misreport against a default
  `OwnerClientId`. Guard order matters.
- Legacy `Input.GetKeyDown` (not the new Input System) — deliberately, to match
  `PlayerCrafting`, which already uses it.
- Toggle semantics: first press equips the first equippable tool, second press unequips.

### The candidate scan — a suggestion, never a decision

```csharp
private string FindFirstEquippableItemId()
{
    for (int i = 0; i < _equipment.Items.Count; i++)      // NetworkList — readable on any peer
    {
        string id = _equipment.Items[i].ItemId.ToString();
        ItemDefinition def = ItemDatabase.GetItem(id);
        if (def != null && def.equippedPrefab != null)
            return id;
    }
    return null;
}
```

Scans **only the equipment `Inventory`** (never `ResourceInventory` — per the prefab's
two-inventory split, tools live in equipment). Reading the `NetworkList` client-side is
safe: it's replicated data. And because the server re-validates everything, it doesn't
matter that this scan runs on an untrusted client — it only picks *which request to send*.

### The ServerRpcs — identity → possession → existence

```csharp
[ServerRpc]
private void EquipServerRpc(string itemId, ServerRpcParams rpcParams = default)
{
    // 1. IDENTITY — only the owner may equip on this player
    if (rpcParams.Receive.SenderClientId != OwnerClientId) { warn; return; }

    if (string.IsNullOrEmpty(itemId)) return;

    // 2. POSSESSION — the player really holds it (checked against the SERVER's copy)
    if (_equipment == null || !_equipment.HasItem(itemId, 1)) { warn; return; }

    // 3. EXISTENCE — the id resolves and the item really is equippable
    ItemDefinition def = ItemDatabase.GetItem(itemId);
    if (def == null || def.equippedPrefab == null) { warn; return; }

    _equippedItemId.Value = new FixedString32Bytes(itemId);   // the ONE authoritative write
}
```

This three-step chain mirrors `Inventory.AddItemServerRpc`'s ownership check and
implements the project rule from `AGENTS.md`: shared world objects accept
`RequireOwnership=false` Rpcs by design, which is exploitable by design — so the server
**never trusts client-sent ids or amounts**. Without step 2, a cheating client could
"equip" an axe it never crafted. Without step 3, it could equip a garbage id and every
client would try to resolve a nonexistent prefab.

**The item stays in the inventory** (our design decision): equipping is a *flag*, not a
transfer — no bookkeeping on unequip/death/drop later.

`UnequipServerRpc` is the same pattern minus the item checks: owner-only, then clear the
value to `default` ("").

### Visuals — every peer, every change

```csharp
private void UpdateHeldVisual()
{
    if (_heldInstance != null) { Destroy(_heldInstance); _heldInstance = null; }

    string id = _equippedItemId.Value.ToString();
    if (string.IsNullOrEmpty(id)) return;                       // unequipped — done

    ItemDefinition def = ItemDatabase.GetItem(id);
    if (def == null || def.equippedPrefab == null) { warn; return; }   // loud failure
    if (handSocket == null) { warn; return; }                          // loud failure

    _heldInstance = Instantiate(def.equippedPrefab, handSocket, worldPositionStays: false);
}
```

- **Destroy-then-recreate, unconditionally** — simpler and more robust than diffing
  ("is the new item the same as the old?"). NGO doesn't even fire `OnValueChanged` when
  the value is identical, so double-equips are a non-issue.
- **`worldPositionStays: false`** — the prefab lands with its authored local transform
  relative to the socket. Combined with the golden rule (root at 0,0,0, pivot at grip)
  the axe *is* at the hand. This is the exact line Bug C and Bug D ambushed.
- **Loud warnings for every silent failure mode** (missing prefab, missing socket,
  missing `ItemDatabaseLoader` scene object). This philosophy came from the GroundMist
  bug: a prefab *asset* dragged into the `nightOnlyObjects` array instead of a *scene
  instance* — `SetActive` happily toggled an object that wasn't in the scene, nothing
  appeared, and nothing complained. Silent failure modes get early-warning logs.

### Why the held axe must NOT be a NetworkObject

Tempting alternative: network-spawn the axe and let a `NetworkTransform` follow the
hand. That's a classic trap:

1. **Double traffic** — syncing an object whose every position/rotation is already
   implied by its parent.
2. **Fighting the Animator** — the hand bone is animated; the axe's world pose changes
   every frame *because of the parent*. A network-synced transform would lag-lerp
   behind it: the classic wobbly-held-weapon bug.
3. **Determinism already solves it** — every client runs the same Animator on the same
   avatar, so the hand is in the same pose everywhere; a child of the hand is therefore
   in the same place everywhere. The id replicates; the prefab resolves locally; the
   rest is free.

End-to-end flow:

```
Owner presses Q
  → client scans replicated equipment Inventory → finds "stone_axe"
  → EquipServerRpc("stone_axe")                       [request only]
  → SERVER: identity ✓ possession ✓ existence ✓
  → _equippedItemId.Value = "stone_axe"               [the one authoritative write]
  → NGO replicates the NetworkVariable to every observer
  → EVERY client: OnEquippedChanged → UpdateHeldVisual
  → destroy old visual → Instantiate(prefab from local ItemDatabase)
  → parented under HandSocket → follows the animated hand bone on every screen
Second Q → UnequipServerRpc → value "" → every client destroys the visual
```

## 2.6 Phase 3 — `EquipmentSetup.cs` (the editor automation)

Per `AGENTS.md`, editing Unity YAML (`.prefab`) by hand is error-prone; prefab surgery
belongs in an editor script using `PrefabUtility` + `SerializedObject` — the same pattern
as `NetworkingSetup.cs`. `Tools > OakHaven > Setup Equipment`:

1. **Find the prefab by search, not path** — `AssetDatabase.FindAssets("NetworkPlayer
   t:Prefab")`. The prefabs folder got reorganized into subfolders (`Character/`,
   `Items/`, ...) and any hardcoded path is one drag-and-drop away from breaking
   (this exact thing broke `NetworkingSetup`'s old `"Assets/Prefabs/NetworkPlayer.prefab"`
   path).
2. **`PrefabUtility.LoadPrefabContents(path)`** — opens an isolated, fully-expanded copy
   of the prefab (including nested instances like the Ch18 model) for editing. This is
   the sanctioned API: no hand YAML edits, and adding a child under a *nested instance's*
   bone saves correctly as an added-object override.
3. **Find the right-hand bone robustly** — walks every `Transform`, normalizes names
   (strips `mixamorig:` prefixes, underscores, case), and prefers **exact** matches on
   **active** objects. The active-preference exists because the prefab contains *two*
   rigs — the visible Ch18 and the disabled Y Bot — and both have a bone effectively
   named "RightHand". A naive search could easily have put the socket on the invisible
   model's hand.
4. **Never fail silently** — if no hand bone matches, it prints *every* bone containing
   "hand" (with active flags) and aborts **without saving**, so nothing ends up
   half-wired.
5. **Create `HandSocket`** under the bone (zero pos/rot, matched layer) — idempotent:
   reuses the existing socket if present, so re-running never duplicates.
6. **Add `PlayerEquipment` if missing, wire `handSocket` via `SerializedObject`** —
   the field is private-serialized, so `FindProperty("handSocket").objectReferenceValue`
   is the correct way (the `NetworkingSetup` house pattern).
7. **Save** — `SaveAsPrefabAsset` + `UnloadPrefabContents`.
8. **Assign `stone_axe.prefab` to `ItemDef_StoneAxe.equippedPrefab`** — looked up by
   `itemId == "stone_axe"` among all `ItemDefinition` assets (the id is the item's real
   identity; filenames lie).
9. **Validate the scene** — warns if no `ItemDatabaseLoader` exists (required in any
   inventory/crafting scene, per `AGENTS.md`), then prints a final checklist.

Idempotent end-to-end: safe to re-run after `Rebuild Player Prefab` (which we now warn
about — see below).

## 2.7 The `NetworkingSetup.cs` path fix

Collateral damage from the prefab-folder reorganization: `NetworkingSetup` hardcoded
`"Assets/Prefabs/NetworkPlayer.prefab"`, a path that no longer exists. Consequences we
prevented: `Setup Networking` would have *created a fresh, basic capsule prefab at the
old path* (a duplicate player prefab — half the networking then pointing at the wrong
one), and `Rebuild Player Prefab` would have destroyed-and-rebuilt the wrong asset.

Fix: both menu flows now resolve the prefab via `FindAssets("NetworkPlayer t:Prefab")`
(`FindExistingPlayerPrefab()` helper), rebuild **in place** at wherever the prefab
currently lives, and the rebuild path warns loudly that it *replaces* hand-tuned
children (like the HandSocket) and tells you to re-run Setup Equipment afterwards.

## 2.8 Bug history & the lessons (recap)

| # | Bug | Symptom | Root cause | Lesson |
|---|---|---|---|---|
| A | Corrupt curve tangents | dusk lighting dipped/overshot wildly | negative slopes on a rising segment | curves are runtime code; `Clamp01` the evaluation |
| B | Dead zone | ~2s of no directional light at dusk | sun dies at 0.77, moon rose at 0.80 | align curves so crossfades overlap, never gap |
| C | Axe 700 m away | "equipped" logged, nothing in hand | world position baked into prefab root | held-item prefab root = (0,0,0), pivot at grip |
| D | Socket teleported | same as C, after tuning | world coords pasted into a *local* slot | Inspector fields on a child are local to its parent |
| E | GroundMist never showed | night-only VFX absent | prefab **asset** dragged into array, no scene instance | drag **Hierarchy instances**; log every silent failure |
| F | Per-second lighting pops on clients | world "snapped" once a second | NetworkVariable hard-set latency-stale clock | absorb small corrections, snap only big ones |
| G | NRE spam / no lighting on relay failure | exceptions pre-spawn | `Update` runs before `OnNetworkSpawn` | guard `IsSpawned`; build visuals state in `Awake` |

## 2.9 Test checklist (the whole chain, end to end)

1. Unity compiles with zero errors; `PlayerEquipment` is on the NetworkPlayer prefab with
   `handSocket` wired (run `Tools > OakHaven > Setup Equipment` if in doubt).
2. `ItemDef_StoneAxe.equippedPrefab` = the axe prefab; the prefab root's **position reads
   (0,0,0)**; the socket's **local position reads (0,0,0)**.
3. Host game → gather 5 wood + 7 stone → repair workbench if needed → craft Stone Axe
   (check console: `[Workstation] Client 0 crafted 1x stone_axe` — it lands in the
   *equipment* Inventory).
4. Press **Q** → console: `[Equipment] Client 0 equipped 'stone_axe'.` → axe visible in
   the right hand (orientation = socket tuning; use the Copy Component → Paste Component
   Values workflow from §2.3 if it needs adjusting).
5. Press **Q** again → axe gone.
6. Second client connected → the other player sees the axe appear/disappear on your
   character without any action on their side.

## 2.10 Where this can go next (Phase 4 backlog)

- **Swing animation**: a chop state on the existing `UpperBodyMask` avatar-mask layer,
  triggered from `PlayerGathering` while `IsEquipped`.
- **Tool-gated gathering**: server rejects chopping when `EquippedItemId` isn't a
  chopping tool — one more validation line in the gather ServerRpc.
- **Grip IK**: install Animation Rigging *only if* the static hand pose on the handle
  looks bad.
- **More items**: any future held item = model with pivot-at-grip + prefab root (0,0,0)
  + `ItemDefinition.equippedPrefab` assignment. Zero new code.

---

*End of Deep Dive #1.*
