using System;
using GameArchitecture.Tuning;
using UnityEngine;

namespace Gameplay.Player
{
    /// <summary>Stamina tuning values; lives in <see cref="BikeConfig"/>.</summary>
    [Serializable]
    public class StaminaSettings
    {
        [Tunable(TuningTabs.Bike, TuningPaths.Stamina)]
        [Tooltip("Seconds of non-stop pedalling in FIRST gear, from full stamina, before running out. " +
                 "Higher gears run out sooner (the Gear Overview's Stamina column shows each gear).")]
        [Min(1f)] public float pedallingSeconds = 17f;

        [Tunable(TuningTabs.Bike, TuningPaths.Stamina)]
        [Tooltip("Seconds of rest to refill stamina from empty to full.")]
        [Min(0.1f)] public float refillSeconds = 10f;

        [Tunable(TuningTabs.Bike, TuningPaths.Stamina)]
        [Tooltip("Seconds without pedalling before stamina starts to refill.")]
        [Min(0f)] public float refillDelay = 1f;

        [Tunable(TuningTabs.Bike, TuningPaths.Stamina)]
        [Tooltip("Below this much stamina (%), the rider gets tired and the pedal bar starts slowing down. " +
                 "Running out completely makes the rider exhausted until stamina refills back to this level.")]
        [Range(0f, 100f)] public float tiredBelowPercent = 30f;

        [Tunable(TuningTabs.Bike, TuningPaths.Stamina)]
        [Tooltip("Pedal bar speed when exhausted, as % of normal. 100 = no penalty for running out.")]
        [Range(1f, 100f)] public float exhaustedBarSpeedPercent = 40f;
    }

    /// <summary>
    /// Rider stamina. Pedalling drains it (heavier gears drain more), resting refills it,
    /// and a tired rider's pedal bar moves slower. Running out entirely makes the rider exhausted:
    /// pedalling is free, but the bar crawls until stamina is back to the tired level.
    /// Reads its settings live, so changes in the Tuning window apply immediately.
    /// </summary>
    public class Stamina
    {
        private readonly StaminaSettings _s;
        private float _restTimer;

        public Stamina(StaminaSettings settings)
        {
            _s = settings;
            Normalized = 1f;
        }

        /// <summary>Stamina from 0 (empty) to 1 (full).</summary>
        public float Normalized { get; private set; }
        /// <summary>True after running out, until stamina is back to the tired level.</summary>
        public bool IsRecovering { get; private set; }
        public bool IsTired => IsRecovering || Normalized < TiredLevel;

        private float TiredLevel => _s.tiredBelowPercent / 100f;
        private float ExhaustedBarSpeed => Mathf.Clamp(_s.exhaustedBarSpeedPercent, 1f, 100f) / 100f;
        private float RefillPerSecond => 1f / Mathf.Max(_s.refillSeconds, 0.1f);

        /// <summary>Multiplier for the pedal bar's speed: 1 when rested, down to the exhausted speed at zero stamina and while exhausted.</summary>
        public float BarSpeedMultiplier
        {
            get
            {
                if (IsRecovering) return ExhaustedBarSpeed;
                if (TiredLevel <= 0f) return 1f;
                return Mathf.Lerp(ExhaustedBarSpeed, 1f, Mathf.Clamp01(Normalized / TiredLevel));
            }
        }

        /// <param name="exerting">Whether the rider pedalled this step.</param>
        /// <param name="effort">How heavy the work is; 1 = lowest gear.</param>
        public void Tick(bool exerting, float effort, float dt)
        {
            if (IsRecovering)
            {
                // Pedalling is free and doesn't interrupt recovery, but the bar is slow (see BarSpeedMultiplier)
                Normalized = Mathf.Min(Normalized + RefillPerSecond * dt, 1f);
                if (Normalized >= TiredLevel)
                    IsRecovering = false;
                return;
            }

            if (exerting)
            {
                _restTimer = 0f;
                Normalized = Mathf.Max(Normalized - effort / Mathf.Max(_s.pedallingSeconds, 1f) * dt, 0f);
                if (Normalized <= 0f)
                    IsRecovering = true;
                return;
            }

            _restTimer += dt;
            if (_restTimer >= _s.refillDelay)
                Normalized = Mathf.Min(Normalized + RefillPerSecond * dt, 1f);
        }
    }
}
