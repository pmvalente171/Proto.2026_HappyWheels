using GameArchitecture.Tuning;
using UnityEngine;

namespace Gameplay.Player
{
    /// <summary>
    /// All the bike's tuning values in one asset. Changes made during play are kept (it's an asset, not a
    /// scene object), and the Tuning window (Window > Tuning) shows every field marked [Tunable].
    /// </summary>
    [CreateAssetMenu(menuName = "Gameplay/Bike Config", fileName = "BikeConfig")]
    public class BikeConfig : ScriptableObject
    {
        [Header("Steering")]
        [Tunable(TuningTabs.Bike, TuningPaths.Steering)]
        [Tooltip("Smallest circle the bike can turn in at walking pace, as a radius in metres. " +
                 "At speed, turns get wider on their own so the tires don't skid (see Cornering Grip). " +
                 "Reference: real bike ≈ 2 m, BMX ≈ 1 m.")]
        [Min(0.2f)] public float tightestTurnRadius = 2f;

        [Tunable(TuningTabs.Bike, TuningPaths.Steering)]
        [Tooltip("How hard the tires can grip in a turn, in g (1 g = the pull of gravity). Sets how tight you can turn at speed: " +
                 "higher = tighter fast turns. Turns never cost speed. " +
                 "Reference: bike on gravel ≈ 0.4, on asphalt ≈ 0.8, racing tires ≈ 1.0.")]
        [Range(0.05f, 2f)] public float corneringGrip = 0.6f;

        [Tunable(TuningTabs.Bike, TuningPaths.Steering)]
        [Tooltip("Seconds to go from riding straight to turning fully (and back). Lower = snappier, higher = smoother and heavier.")]
        [Min(0.01f)] public float steeringSeconds = 0.3f;

        [Tunable(TuningTabs.Bike, TuningPaths.Steering, ReadOnly = true)]
        [Tooltip("Tightest turn at a few speeds, worked out from the settings above.")]
        public TurnInfo[] turnOverview = System.Array.Empty<TurnInfo>();

        /// <summary>Cornering grip as a sideways acceleration (m/s²).</summary>
        public float CorneringGrip => corneringGrip * Physics.gravity.magnitude;

        /// <summary>Tightest turn the bike can make at a speed (m/s), as curvature (1 / radius in metres).</summary>
        public float MaxTurnCurvature(float speed) =>
            Mathf.Min(1f / Mathf.Max(tightestTurnRadius, 0.2f), CorneringGrip / Mathf.Max(speed * speed, 0.0001f));

        [Header("Pedalling")]
        public DrivetrainSettings drivetrain = new();

        [Header("Stamina")]
        public StaminaSettings stamina = new();

        [Header("Braking")]
        [Tunable(TuningTabs.Bike, TuningPaths.Braking)]
        [Tooltip("Speed lost per second at full brake, in km/h per second. E.g. 20 = stops from 40 km/h in 2 seconds. " +
                 "Brake harder than the tires can grip (roughly above 20) and the wheel locks and skids. " +
                 "Reference: gentle brake ≈ 5, normal stop ≈ 10, emergency stop ≈ 20.")]
        [Min(0f)] public float brakeKmhPerSecond = 21.6f;

        [Tunable(TuningTabs.Bike, TuningPaths.Braking)]
        [Tooltip("Speed lost per second when coasting (not pedalling) on flat ground, in km/h per second. " +
                 "E.g. 0.5 = coasting from 20 km/h takes 40 seconds to stop. 0 = coasts forever. " +
                 "Reference: road bike on asphalt ≈ 0.2, mountain bike on dirt ≈ 0.5, on grass ≈ 1.5. " +
                 "The HUD shows the actual coasting slowdown while you're not pedalling.")]
        [Min(0f)] public float coastingKmhPerSecond = 0.2f;

        public float BrakeDeceleration => brakeKmhPerSecond / 3.6f;   // m/s²
        public float RollingResistance => coastingKmhPerSecond / 3.6f; // m/s²

        // Runs in the editor whenever a value changes (Inspector or Tuning window) and when the asset loads
        private void OnValidate()
        {
            drivetrain?.RefreshGearOverview(stamina);
            RefreshTurnOverview();
        }

        private static readonly float[] OverviewSpeedsKmh = { 10f, 20f, 30f, 45f };

        private void RefreshTurnOverview()
        {
            if (turnOverview == null || turnOverview.Length != OverviewSpeedsKmh.Length)
                turnOverview = new TurnInfo[OverviewSpeedsKmh.Length];

            for (int i = 0; i < OverviewSpeedsKmh.Length; i++)
            {
                float speed = OverviewSpeedsKmh[i] / 3.6f;
                float radius = 1f / MaxTurnCurvature(speed);
                turnOverview[i] = new TurnInfo
                {
                    speedKmh = OverviewSpeedsKmh[i],
                    radiusMetres = radius,
                    uTurnSeconds = Mathf.PI * radius / speed,
                };
            }
        }
    }

    /// <summary>One row of <see cref="BikeConfig.turnOverview"/>.</summary>
    [System.Serializable]
    public struct TurnInfo
    {
        [Tooltip("Riding speed (km/h).")]
        [InspectorName("Speed km/h")] public float speedKmh;

        [Tooltip("Radius of the tightest turn at this speed (metres).")]
        [InspectorName("Radius m")] public float radiusMetres;

        [Tooltip("Seconds to turn around completely (180°) at full lock at this speed.")]
        [InspectorName("U-turn s")] public float uTurnSeconds;
    }
}
