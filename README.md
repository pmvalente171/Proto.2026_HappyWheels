# Proto 2026 – Happy Wheels (shelved)

A bicycle delivery game prototype: deliver packages around a city, and hit something too hard and the rider goes
flying as a ragdoll. **Status: shelved.** The pedalling works, but it didn't add enough to the game's real focus,
which is what you do between the pedalling. This README records how it all works, in case any part of it is worth
reusing.

> **Note:** this project was vibe coded. Much of the code was written by an AI assistant (Claude), guided by
> design direction and playtest feedback rather than careful engineering. Read and test anything before you
> reuse it.

Unity 6000.3, URP, Input System. Open `Assets/Scenes/SampleScene.unity` and press Play. A random city is built
when the game starts (see [Test level](#test-level)), and the first delivery job appears.

## Controls

| Key | Action |
|---|---|
| Q / E | Left / right pedal. Hold the key whose stroke is in progress (shown on the HUD bar) |
| ← / → | Steer |
| W / S | Shift gear up / down |
| Space | Brake |
| R | Get back on the bike after a crash (or reset if stuck) |

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
├─ CrashController               detects crashes, ragdolls the rider, respawns
│   └─ RiderBody                 the rider made of boxes; seated visuals or ragdoll (plain C# class)
├─ BikeJuice                     wheel/fork/crank animation, effects, sounds, camera shake, slow motion
├─ InputBootstrap                owns the generated GameInput actions
└─ PedalBarDebugUI               temporary IMGUI HUD: pedal bar, stamina, gear, speed

BicycleRig (spawned, unparented) visuals only while riding: no Rigidbody, no colliders, knows nothing about the player
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
- It also lists one **delivery spot** on the sidewalk along each side of every block, for the delivery loop.

## Crashes and ragdolls

`Gameplay/Player/CrashController.cs`, on the player prefab. A seated rider made of boxes (`RiderBody`) sits on the
bike. When the sphere hits something:

- **Steep surfaces** (buildings, walls, car sides): the speed going *into* the surface is compared with *Wall
  Crash Km/h* (22). Glancing hits only count the head-on part of the speed.
- **Speed bumps** (yellow and black, marked with the `CrashBump` component): riding over one faster than *Bump
  Crash Km/h* (28) throws the rider over the handlebars.
- **Ground-like surfaces** (landings, curbs, ramps): the falling speed is compared with *Hard Landing Km/h* (35,
  about a 5 m drop).
- **Light objects** (under 20 kg, e.g. cones) are knocked away and never cause a crash.

On a crash, the ball freezes. The rider turns into a ragdoll (rigidbodies plus character joints) that keeps the
bike's momentum, with a little pop upwards, and the bike gets a box collider and tumbles too. After *Respawn
Seconds* (3), or straight away on **R**, everything is reset at a spot where the bike was riding normally about
1.5 s before the crash.

## Delivery loop

`Gameplay/Level/DeliveryManager.cs`, on the *Street Level Generator* object:

1. Ride to the **blue marker** to pick up a package. It appears on the bike's rear rack.
2. Ride to the **yellow marker** before the timer runs out. Distance ÷ *Expected Speed* + *Extra Seconds* gives the
   time limit. The drop-off is between *Min* and *Max Delivery Metres* from the pickup.
3. Get paid $5 + $1 per 20 m + $1 per second left, and the next job appears.
4. **Crash while carrying the package** and it goes flying: the delivery fails and a new job is handed out when you
   respawn. Running out of time also fails it.

The HUD at the top of the screen shows money, deliveries, the current task and timer, and an arrow plus the
distance to the target.

## Juice

All feedback, with no effect on handling. Everything is built in code (primitives, generated particles,
synthesised sounds), so no assets are needed. It lives in `Gameplay/Juice`.

- **Bike** (`BikeJuice`, on the player prefab):
  - wheels spin, with spokes so you can see it;
  - the handlebar and front wheel turn with the steering;
  - cranks and pedals turn with the pedal bar, and the rider's legs follow the pedals and their body sways with each
    stroke.
- **Lean** (`BicycleDriver`): the bike leans into turns like a real one, by atan(speed × turn rate ÷ g), up to *Max
  Lean Degrees*.
- **Camera** (`CameraJuice`, added to the main camera automatically):
  - shake on impacts, cones and crashes;
  - a field-of-view kick that grows with speed.
  - It's applied just before rendering and undone after, so it doesn't fight Cinemachine.
- **Effects** (`JuiceFx`): skid marks and tyre smoke, dust on impacts and crashes, sparks off knocked cones, a poof
  on respawn, a burst on pickup and confetti on delivery.
- **Sound** (`JuiceAudio`, synthesised placeholders):
  - while riding: wind that gets louder with speed, freewheel ticking while coasting, skid hiss;
  - impacts and crashes: thuds, a bonk for cones, a whoosh on a crash;
  - deliveries: a chime on pickup, a bell on delivery, a fail buzz, and a countdown tick for the last 5 seconds.
- **Crash slow motion**: time drops to 25% and eases back over about 1 second.
- **HUD**:
  - the pedal bar flashes and pops on every completed rotation;
  - money rolls up in green;
  - messages punch in and fade out;
  - the timer pulses red under 10 seconds;
  - the pickup marker pulses, with a spinning package floating over it.

*BikeJuice* settings: FOV kick and its speed, shake strength, crash slow motion on or off, and volume.

## Known limitations

- The HUDs (`PedalBarDebugUI`, `DeliveryManager`) are throwaway IMGUI.
- The rider is a fixed pose with no IK. Only the legs follow the pedals; the hands don't follow the handlebar
  when it turns.
- Sounds are synthesised placeholders.
- Crash detection only looks at the sphere (the wheel). Something that would only hit the rider's head or body
  (a low beam, for example) doesn't count.
- Steering always stays within the grip, so you can't skid by steering. Braking and landings can still skid.
- There is no air drag, on purpose, so each gear's top speed means exactly what the setting says.
