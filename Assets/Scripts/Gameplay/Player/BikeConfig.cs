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
        [Tooltip("Distance between the wheel axles (m). Longer = wider turns.")]
        [Min(0.1f)] public float wheelbase = 1.05f;

        [Tunable(TuningTabs.Bike, TuningPaths.Steering)]
        [Tooltip("Max handlebar angle (degrees, Y axis) at a given forward speed (m/s, X axis). Lower at speed = harder to turn fast.")]
        public AnimationCurve maxSteerAngle = new(
            new Keyframe(0f, 35f), new Keyframe(3f, 20f), new Keyframe(6f, 10f),
            new Keyframe(12f, 5f), new Keyframe(20f, 3f));

        [Tunable(TuningTabs.Bike, TuningPaths.Steering)]
        [Tooltip("How fast the handlebar turns, in degrees per second.")]
        [Min(1f)] public float steerRate = 120f;

        [Tunable(TuningTabs.Bike, TuningPaths.Steering)]
        [Tooltip("When slower than this (m/s), turning behaves as if moving at this speed, so a stopped bike can still pivot slowly. 0 = fully realistic: no turning without moving.")]
        [Min(0f)] public float minTurnSpeed = 0.3f;

        [Header("Pedalling")]
        public DrivetrainSettings drivetrain = new();

        [Header("Stamina")]
        public StaminaSettings stamina = new();

        [Header("Braking")]
        [Tunable(TuningTabs.Bike, TuningPaths.Braking)]
        [Tooltip("Deceleration at full brake (m/s²). Brake harder than the friction allows and the wheel locks and skids.")]
        [Min(0f)] public float brakeDeceleration = 6f;

        [Tunable(TuningTabs.Bike, TuningPaths.Braking)]
        [Tooltip("Constant slowdown from the tires rolling (m/s²), so the bike coasts to a stop on flat ground.")]
        [Min(0f)] public float rollingResistance = 0.15f;

        [Header("Feedback")]
        [Tunable(TuningTabs.Bike, TuningPaths.Feedback)]
        [Tooltip("Sliding speed at the contact patch (m/s) above which the bike counts as skidding.")]
        [Min(0f)] public float skidSlipSpeed = 0.5f;
    }
}
