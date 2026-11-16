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
        [Min(1f)] public float max = 100f;

        [Tunable(TuningTabs.Bike, TuningPaths.Stamina)]
        [Tooltip("Stamina used per second of pedalling in the lowest gear. Higher gears multiply this by how much heavier they are (e.g. ×2.6 in top gear with the default gears).")]
        [Min(0f)] public float drainPerSecond = 6f;

        [Tunable(TuningTabs.Bike, TuningPaths.Stamina)]
        [Tooltip("Stamina recovered per second while not pedalling, or at any time during recovery.")]
        [Min(0f)] public float recoveryPerSecond = 10f;

        [Tunable(TuningTabs.Bike, TuningPaths.Stamina)]
        [Tooltip("Seconds without pedalling before stamina starts to recover (not used during recovery).")]
        [Min(0f)] public float recoveryDelay = 1f;

        [Tunable(TuningTabs.Bike, TuningPaths.StaminaTired)]
        [Tooltip("Below this fraction of stamina, the pedal bar starts moving slower.")]
        [Range(0f, 1f)] public float tiredThreshold = 0.3f;

        [Tunable(TuningTabs.Bike, TuningPaths.StaminaTired)]
        [Tooltip("Fraction of the normal pedal bar speed left at zero stamina and during recovery (1 = no penalty).")]
        [Range(0.01f, 1f)] public float exhaustedBarSpeed = 0.4f;

        [Tunable(TuningTabs.Bike, TuningPaths.StaminaTired)]
        [Tooltip("After running out, recovery lasts until stamina is back to this fraction. Pedalling costs nothing meanwhile, but the bar moves at Exhausted Bar Speed.")]
        [Range(0.05f, 1f)] public float recoverUntil = 0.5f;
    }

    /// <summary>
    /// Rider stamina. Pedalling drains it (heavier gears drain more), resting recovers it,
    /// and a tired rider's pedal bar moves slower. Running out entirely starts a recovery period:
    /// pedalling is free, but the bar crawls until stamina is back to Recover Until.
    /// Reads its settings live, so changes in the Tuning window apply immediately.
    /// </summary>
    public class Stamina
    {
        private readonly StaminaSettings _s;
        private float _restTimer;
        private float _current;

        public Stamina(StaminaSettings settings)
        {
            _s = settings;
            _current = Max;
        }

        public float Max => Mathf.Max(_s.max, 1f);
        /// <summary>Clamped in case Max was lowered during play.</summary>
        public float Current => Mathf.Min(_current, Max);
        /// <summary>Stamina from 0 (exhausted) to 1 (full).</summary>
        public float Normalized => Current / Max;
        /// <summary>True after running out, until stamina is back to Recover Until.</summary>
        public bool IsRecovering { get; private set; }
        public bool IsTired => IsRecovering || Normalized < _s.tiredThreshold;

        /// <summary>Multiplier for the pedal bar's speed: 1 when rested, down to Exhausted Bar Speed at zero stamina and during recovery.</summary>
        public float BarSpeedMultiplier
        {
            get
            {
                if (IsRecovering) return _s.exhaustedBarSpeed;
                if (_s.tiredThreshold <= 0f) return 1f;
                return Mathf.Lerp(_s.exhaustedBarSpeed, 1f, Mathf.Clamp01(Normalized / _s.tiredThreshold));
            }
        }

        /// <param name="exerting">Whether the rider pedalled this step.</param>
        /// <param name="effort">How heavy the work is; 1 = lowest gear.</param>
        public void Tick(bool exerting, float effort, float dt)
        {
            _current = Current;

            if (IsRecovering)
            {
                // Pedalling is free and doesn't interrupt recovery, but the bar is slow (see BarSpeedMultiplier)
                _current = Mathf.Min(_current + _s.recoveryPerSecond * dt, Max);
                if (Normalized >= _s.recoverUntil)
                    IsRecovering = false;
                return;
            }

            if (exerting)
            {
                _restTimer = 0f;
                _current = Mathf.Max(_current - _s.drainPerSecond * effort * dt, 0f);
                if (_current <= 0f)
                    IsRecovering = true;
                return;
            }

            _restTimer += dt;
            if (_restTimer >= _s.recoveryDelay)
                _current = Mathf.Min(_current + _s.recoveryPerSecond * dt, Max);
        }
    }
}
