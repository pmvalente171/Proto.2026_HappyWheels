using System;
using GameArchitecture.Tuning;
using UnityEngine;

namespace Gameplay.Player
{
    /// <summary>Pedalling and gear tuning values; lives in <see cref="BikeConfig"/>.</summary>
    [Serializable]
    public class DrivetrainSettings
    {
        [Tunable(TuningTabs.Bike, TuningPaths.DrivetrainGears)]
        [Tooltip("Metres per pedal rotation for each gear, lowest to highest. Sets the bar's length, how long a rotation takes, and the gear's top speed.")]
        public float[] gears = { 2.5f, 3.5f, 4.5f, 5.5f, 6.5f };

        [Tunable(TuningTabs.Bike, TuningPaths.DrivetrainGears)]
        [Tooltip("Gear index the bike starts in (0 = lowest).")]
        [Min(0)] public int startGear = 1;

        [Tunable(TuningTabs.Bike, TuningPaths.Drivetrain)]
        [Tooltip("Pedal rotations per second in the lowest gear, when rested. Higher gears have longer bars, so they rotate proportionally slower (the bar moves at the same speed on screen).")]
        [Min(0.1f)] public float lowestGearCadence = 1.5f;

        [Tunable(TuningTabs.Bike, TuningPaths.Drivetrain)]
        [Tooltip("Margin of error when switching pedals, as a fraction of a rotation (0.08 ≈ 29°). Within this distance of either end of the bar, both Q and E move it.")]
        [Range(0f, 0.25f)] public float switchWindow = 0.08f;

        [Tunable(TuningTabs.Bike, TuningPaths.DrivetrainBoost)]
        [Tooltip("Speed burst per completed rotation in the lowest gear (m/s), from a standstill.")]
        [Min(0f)] public float lowestGearBoost = 1.5f;

        [Tunable(TuningTabs.Bike, TuningPaths.DrivetrainBoost)]
        [Tooltip("How much bigger bursts get in heavier gears: burst = Lowest Gear Boost × heaviness^this. " +
                 "0 = same burst in every gear. 1 = bursts grow as fast as rotations slow down, so every gear pushes equally hard on average. " +
                 "Below 1, low gears push harder on average (better for climbing).")]
        [Range(0f, 1f)] public float boostGearScaling = 0.5f;

        [Tunable(TuningTabs.Bike, TuningPaths.DrivetrainBoost)]
        [Tooltip("Each gear's top speed = its metres per rotation × this. Bursts shrink to nothing as the bike approaches it.")]
        [Min(0.1f)] public float topSpeedCadence = 1.5f;

        [Tunable(TuningTabs.Bike, TuningPaths.DrivetrainBoost)]
        [Tooltip("Seconds over which each burst is applied. An instant burst would spin the wheel faster than the tire can grip.")]
        [Min(0.01f)] public float boostDuration = 0.3f;
    }

    /// <summary>
    /// Pedals, cranks and gears. The crank is shown as a bar that swings left and right: one full
    /// rotation is the bar going all the way to one side and back. Holding the pedal whose stroke is in
    /// progress (E while the bar moves right, Q while it moves left) moves the bar; the other key does nothing.
    /// Every completed rotation gives the bike a burst of speed.
    /// Higher gears: longer bar (slower rotations), bigger bursts, higher top speed, more stamina. Good for distance.
    /// Lower gears: shorter bar (faster rotations), smaller bursts but more of them, so a harder average push. Good for climbing.
    /// Reads its settings live, so changes in the Tuning window apply immediately (even to the gear list).
    /// </summary>
    public class Drivetrain
    {
        public enum Pedal { Left, Right }

        private const float FallbackDevelopment = 4f;

        private readonly DrivetrainSettings _s;
        private int _gear;
        private float _boostRemaining; // m/s of burst still to apply
        private float _boostRate;      // m/s² it's applied at

        public Drivetrain(DrivetrainSettings settings)
        {
            _s = settings;
            _gear = Mathf.Clamp(_s.startGear, 0, GearCount - 1);
        }

        /// <summary>Current gear index, 0 = lowest.</summary>
        public int Gear => Mathf.Clamp(_gear, 0, GearCount - 1);
        public int GearCount => _s.gears is { Length: > 0 } ? _s.gears.Length : 1;

        /// <summary>Metres per pedal rotation in the current gear.</summary>
        public float Development => GearDevelopment(Gear);

        /// <summary>Metres per rotation of the highest gear, for scaling the bar's size.</summary>
        public float MaxDevelopment
        {
            get
            {
                float max = 0.01f;
                for (int i = 0; i < GearCount; i++)
                    max = Mathf.Max(max, GearDevelopment(i));
                return max;
            }
        }

        /// <summary>Current gear's top speed (m/s).</summary>
        public float TopSpeed => Development * _s.topSpeedCadence;

        /// <summary>
        /// How much heavier the current gear is than the lowest one (1 = lowest gear).
        /// Rotations are slower by this factor, and stamina drains faster by it.
        /// </summary>
        public float Effort => Development / GearDevelopment(0);

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
                float s = Mathf.Repeat(CrankPhase, 0.5f); // position within the current stroke, 0–0.5
                return s < _s.switchWindow || s > 0.5f - _s.switchWindow;
            }
        }

        /// <summary>Size of the switch window at each end, as a fraction of the bar's length (for drawing it).</summary>
        public float SwitchWindowBarFraction => (1f - Mathf.Cos(_s.switchWindow * 2f * Mathf.PI)) * 0.5f;

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
            Cadence = IsPushing ? _s.lowestGearCadence / Effort * Mathf.Clamp(barSpeedMultiplier, 0.01f, 1f) : 0f;

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
            // Bigger bursts in heavier gears, fading out near the gear's top speed
            float speedFactor = Mathf.Clamp01(1f - Mathf.Max(wheelSpeed, 0f) / Mathf.Max(TopSpeed, 0.01f));
            LastBoost = _s.lowestGearBoost * Mathf.Pow(Effort, _s.boostGearScaling) * speedFactor;

            // If the previous burst is still going, keep its rate unless the new one needs a faster one
            float previousRate = _boostRemaining > 0f ? _boostRate : 0f;
            _boostRemaining += LastBoost;
            _boostRate = Mathf.Max(previousRate, LastBoost / Mathf.Max(_s.boostDuration, 0.01f));
        }

        private float GearDevelopment(int index)
        {
            if (_s.gears is not { Length: > 0 })
                return FallbackDevelopment;
            return Mathf.Max(_s.gears[Mathf.Clamp(index, 0, _s.gears.Length - 1)], 0.01f);
        }
    }
}
