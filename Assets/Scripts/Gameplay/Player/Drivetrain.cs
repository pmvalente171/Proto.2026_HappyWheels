using System;
using GameArchitecture.Tuning;
using UnityEngine;

namespace Gameplay.Player
{
    /// <summary>Pedalling and gear tuning values; lives in <see cref="BikeConfig"/>.</summary>
    [Serializable]
    public class DrivetrainSettings
    {
        // Designer-facing values are in km/h, RPM and % (same units as the on-screen HUD); the game works in m/s
        // and rotations per second, converted by the properties at the bottom.
        private const float KmhToMs = 1f / 3.6f;
        private const float RpmToRps = 1f / 60f;

        [Tunable(TuningTabs.Bike, TuningPaths.DrivetrainGears)]
        [Tooltip("How many gears the bike has (W/S to shift).")]
        [Min(1)] public int gearCount = 5;

        [Tunable(TuningTabs.Bike, TuningPaths.DrivetrainGears)]
        [Tooltip("Top speed in FIRST gear, in km/h: the fastest you can go on flat ground by pedalling in the lowest gear. " +
                 "Pedalling faster than this does nothing, so you have to shift up. " +
                 "Reference: walking ≈ 5 km/h, relaxed cycling ≈ 15 km/h, fast cycling ≈ 30 km/h, racing sprint ≈ 60 km/h.")]
        [Min(1f)] public float firstGearTopSpeedKmh = 18f;

        [Tunable(TuningTabs.Bike, TuningPaths.DrivetrainGears)]
        [Tooltip("Top speed in the LAST gear, in km/h: the fastest the bike can go by pedalling at all. " +
                 "Downhills can still push it past this. " +
                 "Reference: relaxed cycling ≈ 15 km/h, fast cycling ≈ 30 km/h, racing sprint ≈ 60 km/h.")]
        [Min(1f)] public float lastGearTopSpeedKmh = 47f;

        [Tunable(TuningTabs.Bike, TuningPaths.DrivetrainGears)]
        [Tooltip("How the gears in between are spaced (applies to both top speed and boost). " +
                 "X axis: gear, from first (0) to last (1). Y axis: from the first-gear value (0) to the last-gear value (1). " +
                 "Straight line = every shift changes things by the same amount. " +
                 "Sagging below the line = low gears close together (fine control for climbing), big jumps at the top. " +
                 "Bulging above the line = big jumps at the bottom, high gears close together (fine control at speed).")]
        public AnimationCurve gearSpacing = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        [Tunable(TuningTabs.Bike, TuningPaths.DrivetrainGears)]
        [Tooltip("Gear the bike starts in. 1 = first (lowest) gear.")]
        [Min(1)] public int startGear = 2;

        [Tunable(TuningTabs.Bike, TuningPaths.DrivetrainGears, ReadOnly = true)]
        [Tooltip("Preview of every gear, worked out from the Pedalling and Stamina settings. Updates as you edit; can't be edited itself.")]
        public GearInfo[] gearOverview = Array.Empty<GearInfo>();

        [Tunable(TuningTabs.Bike, TuningPaths.Drivetrain)]
        [Tooltip("How fast the pedal bar moves, as pedal RPM in first gear when rested. " +
                 "Higher gears have a longer bar, so they turn slower than this (the bar moves at the same speed on screen). " +
                 "Reference: casual cyclist ≈ 60 RPM, trained cyclist ≈ 90 RPM, sprint ≈ 120 RPM.")]
        [Min(1f)] public float firstGearRpm = 90f;

        [Tunable(TuningTabs.Bike, TuningPaths.Drivetrain)]
        [Tooltip("How forgiving switching between Q and E is: the % of the bar at each end where either key works (the yellow zones). " +
                 "0 = you must switch exactly at the end. 6 ≈ comfortable. 25+ = very forgiving.")]
        [Range(0f, 50f)] public float switchLeniencyPercent = 6f;

        [Tunable(TuningTabs.Bike, TuningPaths.DrivetrainBoost)]
        [Tooltip("Speed gained per completed pedal rotation in FIRST gear, from a standstill, in km/h. " +
                 "E.g. 5 = each rotation adds 5 km/h. Gains shrink as you approach the gear's top speed.")]
        [Min(0f)] public float firstGearBoostKmh = 5.4f;

        [Tunable(TuningTabs.Bike, TuningPaths.DrivetrainBoost)]
        [Tooltip("Speed gained per completed pedal rotation in the LAST gear, from a standstill, in km/h. " +
                 "High gears rotate slower, so if this is about the same as First Gear Boost, low gears accelerate much faster. " +
                 "The Gear Overview's Accel column shows the result.")]
        [Min(0f)] public float lastGearBoostKmh = 8.7f;

        /// <summary>Seconds each rotation's speed gain is spread over. An instant burst would spin the wheel faster than the tire can grip.</summary>
        private const float BoostDuration = 0.3f;

        // ---- Internal units (m/s, rotations per second, 0-based gear index) ----
        public float FirstGearCadence => firstGearRpm * RpmToRps;
        public int StartGearIndex => startGear - 1;
        public int GearCount => Mathf.Max(gearCount, 1);
        public float BoostSeconds => BoostDuration;

        /// <summary>Switch window as a fraction of a rotation (the bar moves slowly near its ends, so this isn't linear in bar %).</summary>
        public float SwitchWindowPhase => Mathf.Acos(1f - 2f * Mathf.Clamp(switchLeniencyPercent, 0f, 50f) / 100f) / (2f * Mathf.PI);

        /// <summary>0 for first gear, 1 for last, spaced by the Gear Spacing curve.</summary>
        private float Spacing(int index)
        {
            float t = GearCount > 1 ? Mathf.Clamp(index, 0, GearCount - 1) / (float)(GearCount - 1) : 0f;
            return gearSpacing is { length: > 0 } ? gearSpacing.Evaluate(t) : t;
        }

        /// <summary>Top speed of a gear (m/s).</summary>
        public float GearTopSpeed(int index) =>
            Mathf.Max(Mathf.LerpUnclamped(firstGearTopSpeedKmh, lastGearTopSpeedKmh, Spacing(index)), 1f) * KmhToMs;

        /// <summary>Speed gained per rotation in a gear from a standstill (m/s).</summary>
        public float GearBoost(int index) =>
            Mathf.Max(Mathf.LerpUnclamped(firstGearBoostKmh, lastGearBoostKmh, Spacing(index)), 0f) * KmhToMs;

        /// <summary>How much heavier a gear is than first gear (1 = first). Its bar is this much longer and turns this much slower.</summary>
        public float GearEffort(int index) => GearTopSpeed(index) / GearTopSpeed(0);

        /// <summary>Recomputes <see cref="gearOverview"/>. Called from the owning asset's OnValidate.</summary>
        public void RefreshGearOverview(StaminaSettings stamina)
        {
            if (gearOverview == null || gearOverview.Length != GearCount)
                gearOverview = new GearInfo[GearCount];

            for (int i = 0; i < GearCount; i++)
            {
                float effort = GearEffort(i);
                float rpm = firstGearRpm / effort;
                float boostKmh = GearBoost(i) * 3.6f;
                gearOverview[i] = new GearInfo
                {
                    gear = i + 1,
                    topSpeedKmh = GearTopSpeed(i) * 3.6f,
                    boostKmh = boostKmh,
                    accelKmhPerSecond = boostKmh * rpm / 60f,
                    barRpm = rpm,
                    staminaSeconds = stamina != null ? stamina.pedallingSeconds / effort : 0f,
                };
            }
        }
    }

    /// <summary>One row of <see cref="DrivetrainSettings.gearOverview"/>.</summary>
    [Serializable]
    public struct GearInfo
    {
        [Tooltip("Gear number (1 = lowest).")]
        public int gear;

        [Tooltip("Fastest you can go on flat ground by pedalling in this gear (km/h).")]
        [InspectorName("Top km/h")] public float topSpeedKmh;

        [Tooltip("Speed gained per completed rotation, from a standstill (km/h).")]
        [InspectorName("Boost km/h")] public float boostKmh;

        [Tooltip("Average acceleration from a standstill while pedalling non-stop when rested (km/h gained per second). " +
                 "Boost × rotations per second. Shrinks as you approach top speed.")]
        [InspectorName("Accel km/h/s")] public float accelKmhPerSecond;

        [Tooltip("How fast the pedal bar turns in this gear when rested (pedal RPM).")]
        [InspectorName("Bar RPM")] public float barRpm;

        [Tooltip("Seconds of non-stop pedalling in this gear, from full stamina, before running out.")]
        [InspectorName("Stamina s")] public float staminaSeconds;
    }

    /// <summary>
    /// Pedals, cranks and gears. The crank is shown as a bar that swings left and right: one full
    /// rotation is the bar going all the way to one side and back. Holding the pedal whose stroke is in
    /// progress (E while the bar moves right, Q while it moves left) moves the bar; the other key does nothing.
    /// Every completed rotation gives the bike a burst of speed.
    /// Higher gears: longer bar (slower rotations), bigger bursts, higher top speed, more stamina. Good for distance.
    /// Lower gears: shorter bar (faster rotations), smaller bursts but more of them, so a harder average push. Good for climbing.
    /// Reads its settings live, so changes in the Tuning window apply immediately (even to the number of gears).
    /// </summary>
    public class Drivetrain
    {
        public enum Pedal { Left, Right }

        private readonly DrivetrainSettings _s;
        private int _gear;
        private float _boostRemaining; // m/s of burst still to apply
        private float _boostRate;      // m/s² it's applied at

        public Drivetrain(DrivetrainSettings settings)
        {
            _s = settings;
            _gear = Mathf.Clamp(_s.StartGearIndex, 0, GearCount - 1);
        }

        /// <summary>Current gear index, 0 = lowest.</summary>
        public int Gear => Mathf.Clamp(_gear, 0, GearCount - 1);
        public int GearCount => _s.GearCount;

        /// <summary>Current gear's top speed (m/s).</summary>
        public float TopSpeed => _s.GearTopSpeed(Gear);

        /// <summary>
        /// How much heavier the current gear is than the lowest one (1 = lowest gear).
        /// Rotations are slower by this factor, and stamina drains faster by it.
        /// </summary>
        public float Effort => _s.GearEffort(Gear);

        /// <summary>Length of the current gear's bar relative to the last gear's (0–1], for drawing it.</summary>
        public float BarLength => TopSpeed / Mathf.Max(_s.GearTopSpeed(GearCount - 1), 0.01f);

        /// <summary>Crank rotation, 0–1. 0 = bar fully left, 0.5 = bar fully right.</summary>
        public float CrankPhase { get; private set; }
        /// <summary>Bar position from -1 (left) to +1 (right).</summary>
        public float BarPosition => -Mathf.Cos(CrankPhase * 2f * Mathf.PI);
        /// <summary>The pedal that moves the current stroke.</summary>
        public Pedal RequiredPedal => CrankPhase < 0.5f ? Pedal.Right : Pedal.Left;

        /// <summary>Pedal rotations per second during the last step (0 when not pushing).</summary>
        public float Cadence { get; private set; }
        /// <summary>True if the correct pedal was held during the last step.</summary>
        public bool IsPushing { get; private set; }
        /// <summary>True while a burst is being applied.</summary>
        public bool IsBoosting => _boostRemaining > 0f;
        /// <summary>Size of the most recent burst (m/s).</summary>
        public float LastBoost { get; private set; }

        public void ShiftUp() => _gear = Mathf.Min(Gear + 1, GearCount - 1);
        public void ShiftDown() => _gear = Mathf.Max(Gear - 1, 0);

        /// <summary>True when the bar is close enough to either end that both pedals move it.</summary>
        public bool InSwitchWindow
        {
            get
            {
                float window = _s.SwitchWindowPhase;
                float s = Mathf.Repeat(CrankPhase, 0.5f); // position within the current stroke, 0–0.5
                return s < window || s > 0.5f - window;
            }
        }

        /// <summary>Size of the switch window at each end, as a fraction of the bar's length (for drawing it).</summary>
        public float SwitchWindowBarFraction => Mathf.Clamp(_s.switchLeniencyPercent, 0f, 50f) / 100f;

        /// <summary>
        /// Moves the bar and returns the drive acceleration (m/s² of bike speed) for this step.
        /// <paramref name="barSpeedMultiplier"/> slows the bar down, e.g. for a tired rider.
        /// </summary>
        public float Step(bool leftHeld, bool rightHeld, float wheelSpeed, bool grounded, float barSpeedMultiplier, float dt)
        {
            bool required = RequiredPedal == Pedal.Left ? leftHeld : rightHeld;
            bool other = RequiredPedal == Pedal.Left ? rightHeld : leftHeld;

            // Margin of error: near either end of the bar, the other pedal also counts. That lets the rider
            // switch keys slightly early, or let go of the old key slightly late, without losing the stroke.
            IsPushing = grounded && (required || (other && InSwitchWindow));

            // The rider moves the bar; a longer bar (higher gear) takes longer to get around
            Cadence = IsPushing ? _s.FirstGearCadence / Effort * Mathf.Clamp(barSpeedMultiplier, 0.01f, 1f) : 0f;

            float phase = CrankPhase + Cadence * dt;
            if (phase >= 1f)
                QueueBoost(wheelSpeed);
            CrankPhase = Mathf.Repeat(phase, 1f);

            // Bursts need the tire on the ground; any left over waits for landing
            if (!grounded || _boostRemaining <= 0f)
                return 0f;

            float accel = Mathf.Min(_boostRate, _boostRemaining / dt);
            _boostRemaining -= accel * dt;
            return accel;
        }

        private void QueueBoost(float wheelSpeed)
        {
            // Burst for this gear, fading out near the gear's top speed
            float speedFactor = Mathf.Clamp01(1f - Mathf.Max(wheelSpeed, 0f) / Mathf.Max(TopSpeed, 0.01f));
            LastBoost = _s.GearBoost(Gear) * speedFactor;

            // If the previous burst is still going, keep its rate unless the new one needs a faster one
            float previousRate = _boostRemaining > 0f ? _boostRate : 0f;
            _boostRemaining += LastBoost;
            _boostRate = Mathf.Max(previousRate, LastBoost / _s.BoostSeconds);
        }
    }
}
