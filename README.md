# Proto 2026 – Happy Wheels (shelved)

A bicycle prototype. **Status: shelved.** The pedalling works, but it didn't add enough to the game's real focus,
which is what you do between the pedalling. This README records how the character controller works, in case any
part of it is worth reusing.

> **Note:** this project was vibe coded. Much of the code was written by an AI assistant (Claude), guided by
> design direction and playtest feedback rather than careful engineering. Read and test anything before you
> reuse it.

Unity 6000.3, URP, Input System. Open `Assets/Scenes/SampleScene.unity` and press Play. A random street level
is built when the game starts (see [Test level](#test-level)).

## Controls

| Key | Action |
|---|---|
| Q / E | Left / right pedal. Hold the key whose stroke is in progress (shown on the HUD bar) |
| ← / → | Steer |
| W / S | Shift gear up / down |
| Space | Brake |

## How it works

The rider is a **rolling sphere with real physics**, and the bicycle you see is a **separate visual-only object**
that follows it.

```
Player_Pre (prefab)
├─ Rigidbody + SphereCollider    the "wheel": PhysX does the rolling, collisions, slopes and gravity
├─ BallMotor                     turns input into spin, steering and braking on the sphere
│   ├─ Drivetrain                pedal bar, gears, speed bursts      (plain C# class)
│   └─ Stamina                   drains while pedalling, slows the bar (plain C# class)
├─ BicycleDriver                 spawns the bike visuals and pushes the sphere's pose into them
├─ InputBootstrap                owns the generated GameInput actions
└─ PedalBarDebugUI               temporary IMGUI HUD: pedal bar, stamina, gear, speed

BicycleRig (spawned, unparented) visuals only: no Rigidbody, no colliders, knows nothing about the player
```

All tuning values are in one asset, `Assets/Data/BikeConfig.asset` (`BikeConfig`). `BallMotor`, `Drivetrain` and
`Stamina` read it live, so edits during play take effect immediately and are kept after play.

### BallMotor (`Gameplay/Player/BallMotor.cs`)

Every physics step:

1. **Ground check**: a sphere cast down gives `IsGrounded` and the ground normal. Steering, pedalling and braking
   only work on the ground.
2. **Steering**: `SteerAmount` (−1…1) moves toward the input over *Steering Seconds*. The heading turns along a
   circle whose tightest radius is the smaller of:
   - *Tightest Turn Radius* (used at low speed);
   - speed² ÷ *Cornering Grip* (used at high speed, so turning never asks for more grip than the tyre has).
3. **Cornering**: the ball's velocity and spin are rotated with the heading, like a tyre rolling along a curve,
   so **turning doesn't cost speed**. If a turn would need more than *Cornering Grip*, the velocity only partly
   follows. Contact friction then scrubs off the sideways slip, which is a skid.
4. **Wheel constraint**: the sphere may only spin around its axle (perpendicular to the heading). Pedalling,
   braking and coasting change that spin, and friction with the ground turns it into speed. The code converts
   between "m/s² of bike speed" and "rad/s of spin" using the sphere's inertia (`ApplyPedalsAndBrakes`).
5. **Skid detection**: if the contact point slides faster than 0.5 m/s, `IsSkidding` is set.

Rigidbody damping is forced to 0 at startup, so all coasting slowdown comes from *Coasting Km/h Per Second*.

### Drivetrain (`Gameplay/Player/Drivetrain.cs`)

- The crank is a **bar that swings left and right**. One rotation is all the way to one side and back. Holding the
  key for the stroke in progress moves the bar; the other key does nothing. The yellow zones at each end
  (*Switch Leniency %*) accept either key, so switching can be slightly early or late.
- **Each completed rotation gives a burst of speed**, spread over 0.3 s so the tyre doesn't slip. Bursts shrink
  to zero as the bike approaches the current gear's top speed.
- **Gears**: each gear's top speed and burst size are set between first-gear and last-gear values, spaced by the
  *Gear Spacing* curve. A gear's **effort** is its top speed ÷ first gear's top speed. In heavier gears the bar is
  that many times longer, turns that many times slower, and drains stamina that many times faster. Low gears give
  many small bursts (for climbing); high gears give fewer, bigger ones (for top speed).

### Stamina (`Gameplay/Player/Stamina.cs`)

- **Pedalling** drains stamina by *effort* per second.
- **Resting** refills it after *Refill Delay*.
- **Below *Tired Below %*** the pedal bar gradually slows down, to *Exhausted Bar Speed %* at empty.
- **Running out** makes the rider exhausted: pedalling costs nothing, but the bar crawls until stamina is back to
  the tired level.

## Tuning

**Window ▸ Tuning** shows every bike setting, grouped into Steering / Pedalling (Gears, Boost) / Stamina / Braking,
with Save and Revert buttons. The same settings show in the Inspector when you select `BikeConfig.asset`.

Every setting is in units you can picture (seconds, metres, km/h, RPM, %, g), and its tooltip gives real-world
reference values. Two read-only tables update as you edit:

- **Gear Overview**: per gear, top speed, burst size, acceleration, bar RPM and seconds of stamina.
- **Turn Overview**: tightest turn radius and U-turn time at 10 / 20 / 30 / 45 km/h.

The HUD also shows the measured coasting slowdown (km/h/s) while you're not pedalling.

### The `[Tunable]` attribute (reusable)

`GameArchitecture/Tuning` doesn't depend on the bike and can be copied to other projects:

```csharp
[Tunable("Bike", "Pedalling/Gears")]                 // tab, folder path
public float firstGearTopSpeedKmh = 18f;

[Tunable("Bike", "Pedalling/Gears", ReadOnly = true)] // shown, not editable (e.g. filled in OnValidate)
public GearInfo[] gearOverview;
```

- It works on fields of ScriptableObjects, including fields inside `[Serializable]` classes they contain.
- **Arrays and lists are drawn as tables** (one row per element, one column per field), both in the Tuning window
  and in the Inspector. Column headers use `[InspectorName]` if the field has one; `[Tooltip]` shows when you
  hover a header.
- The Inspector side is a fallback editor (`TunableAssetEditor`). It only changes ScriptableObjects that have
  `[Tunable]` fields and don't have their own custom editor.

## Test level

`Gameplay/Level/StreetLevelGenerator.cs`, on the *Street Level Generator* object in the scene, builds a random
street grid from primitives on Awake. Nothing is saved to the scene. The city is centred on the object, and the
player starts at the central intersection.

- **Blocks** are buildings, parks with kick ramps, ramps up onto rooftops, or mounds.
- **Roads** get kick ramps, road hills, speed bumps, parked cars or knockable cones.
- **Seed** set to 0 gives a new city each play. The seed is logged to the Console so a good city can be replayed.
- **Hide On Start** switches off the scene's old test ramps and wall.

## Known limitations

- The HUD (`PedalBarDebugUI`) is throwaway IMGUI.
- `BicycleRig` only follows position and rotation. There is no wheel spin, lean, handlebar or rider IK yet.
- Steering always stays within the grip, so you can't skid by steering. Braking and landings can still skid.
- There is no air drag, on purpose, so each gear's top speed means exactly what the setting says.
