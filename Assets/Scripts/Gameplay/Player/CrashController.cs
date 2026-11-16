using System;
using System.Collections.Generic;
using UnityEngine;
using Gameplay.Level;
using UnityEngine.InputSystem;

namespace Gameplay.Player
{
    /// <summary>
    /// Throws the rider off when the bike hits something too hard: the rider becomes a ragdoll, the bike tumbles,
    /// and after a few seconds (or on R) everything respawns at a safe spot from just before the crash.
    /// Also puts a seated rider on the bike. R while riding also respawns (handy when stuck).
    /// </summary>
    [RequireComponent(typeof(BallMotor), typeof(BicycleDriver))]
    public class CrashController : MonoBehaviour
    {
        [Tooltip("Hitting a wall, car or anything solid head-on faster than this (km/h) throws the rider off. " +
                 "Glancing hits only count the part of the speed going into the wall.")]
        [SerializeField, Min(1f)] private float wallCrashKmh = 22f;

        [Tooltip("Landing faster than this (km/h of falling speed) throws the rider off. " +
                 "Reference: 25 km/h ≈ a 2.5 m drop, 35 km/h ≈ 5 m, 45 km/h ≈ 8 m.")]
        [SerializeField, Min(1f)] private float hardLandingKmh = 35f;

        [Tooltip("Riding over a speed bump faster than this (km/h) throws the rider off.")]
        [SerializeField, Min(1f)] private float bumpCrashKmh = 28f;

        [Tooltip("Seconds lying on the ground before respawning. R respawns straight away.")]
        [SerializeField, Min(0.5f)] private float respawnSeconds = 3f;

        // Objects lighter than this (kg) get knocked away instead of crashing the rider (cones, boxes...)
        private const float LightObjectMass = 20f;
        // Impacts below this (km/h) are too small to report (rolling from one surface onto the next)
        private const float MinReportedImpactKmh = 6f;
        // Respawn this long before the crash, so the bike isn't put back right against what it hit
        private const float RespawnRewindSeconds = 1.5f;
        private const float SafePointInterval = 0.5f;
        private const int SafePointCount = 10;

        // Rough size of the bicycle model, for its collider while it tumbles
        private static readonly Vector3 BikeColliderCentre = new(0f, 0.65f, 0f);
        private static readonly Vector3 BikeColliderSize = new(0.4f, 1f, 1.7f);

        private BallMotor _motor;
        private BicycleDriver _driver;
        private Rigidbody _body;
        private RiderBody _rider;
        private RiderBody _ragdoll;

        private readonly List<(Vector3 position, Vector3 heading, float time)> _safePoints = new();
        private Vector3 _startPosition, _startHeading;
        private float _safePointTimer;
        private float _crashTime;
        private Vector3 _lastVelocity;

        /// <summary>True from the crash until the respawn.</summary>
        public bool IsCrashed { get; private set; }

        /// <summary>Raised on a crash, with the bike's velocity just before the impact.</summary>
        public event Action<Vector3> Crashed;
        public event Action Respawned;
        /// <summary>A hit that wasn't hard enough to crash: contact point, impact speed (km/h), and whether it was a wall.</summary>
        public event Action<Vector3, float, bool> Impact;
        /// <summary>Knocked a light object (e.g. a cone) out of the way: contact point and speed (km/h).</summary>
        public event Action<Vector3, float> KnockedObject;

        /// <summary>The rider while seated on the bike, otherwise null.</summary>
        public RiderBody Rider => _rider;

        /// <summary>The bicycle visuals (to attach things to, like a package).</summary>
        public Transform Bike => _driver.Rig ? _driver.Rig.transform : null;

        private void Awake()
        {
            _motor = GetComponent<BallMotor>();
            _driver = GetComponent<BicycleDriver>();
            _body = GetComponent<Rigidbody>();
        }

        private void Start()
        {
            _startPosition = transform.position;
            _startHeading = transform.forward;
            if (Bike)
                _rider = new RiderBody(Bike);
        }

        private void FixedUpdate()
        {
            if (IsCrashed)
                return;

            _lastVelocity = _body.linearVelocity;

            // Remember where the bike was recently riding normally, to respawn there
            _safePointTimer += Time.fixedDeltaTime;
            if (_safePointTimer >= SafePointInterval && _motor.IsGrounded && !_motor.IsSkidding)
            {
                _safePointTimer = 0f;
                _safePoints.Add((_body.position, _motor.Heading, Time.time));
                if (_safePoints.Count > SafePointCount)
                    _safePoints.RemoveAt(0);
            }
        }

        private void Update()
        {
            bool respawnPressed = Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame;
            if (IsCrashed && (respawnPressed || Time.time - _crashTime >= respawnSeconds))
                Respawn();
            else if (!IsCrashed && respawnPressed)
                Respawn(); // stuck: put the bike back on the road
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (IsCrashed || !enabled || collision.contactCount == 0)
                return;

            if (collision.rigidbody && !collision.rigidbody.isKinematic && collision.rigidbody.mass < LightObjectMass)
            {
                KnockedObject?.Invoke(collision.GetContact(0).point, collision.relativeVelocity.magnitude * 3.6f);
                return;
            }

            // Speed bumps: too fast over one and the rider goes over the handlebars
            float groundSpeedKmh = Vector3.ProjectOnPlane(_lastVelocity, Vector3.up).magnitude * 3.6f;
            if (groundSpeedKmh > bumpCrashKmh && collision.collider.GetComponent<CrashBump>())
            {
                Crash(_lastVelocity);
                return;
            }

            float strongest = 0f;
            Vector3 strongestPoint = collision.GetContact(0).point;
            bool strongestIsWall = false;
            for (int i = 0; i < collision.contactCount; i++)
            {
                ContactPoint contact = collision.GetContact(i);
                Vector3 normal = contact.normal;
                float impact = Mathf.Abs(Vector3.Dot(collision.relativeVelocity, normal)) * 3.6f; // km/h into the surface

                // Steep surfaces (walls, buildings, car sides) vs ground-like surfaces (landings). Curbs and ramps count as ground.
                bool wall = Mathf.Abs(normal.y) < 0.5f;
                if ((wall && impact > wallCrashKmh) || (!wall && impact > hardLandingKmh))
                {
                    Crash(_lastVelocity);
                    return;
                }

                if (impact > strongest)
                {
                    strongest = impact;
                    strongestPoint = contact.point;
                    strongestIsWall = wall;
                }
            }

            if (strongest > MinReportedImpactKmh)
                Impact?.Invoke(strongestPoint, strongest, strongestIsWall);
        }

        private void Crash(Vector3 velocity)
        {
            IsCrashed = true;
            _crashTime = Time.time;

            _motor.SetFrozen(true);
            _driver.Following = false;

            // The rider flies off...
            _ragdoll = _rider;
            _rider = null;
            _ragdoll?.Ragdoll(velocity);

            // ...and the bike tumbles on its own
            if (Bike)
            {
                var box = Bike.gameObject.AddComponent<BoxCollider>();
                box.center = BikeColliderCentre;
                box.size = BikeColliderSize;
                var bikeBody = Bike.gameObject.AddComponent<Rigidbody>();
                bikeBody.mass = 15f;
                bikeBody.interpolation = RigidbodyInterpolation.Interpolate;
                bikeBody.linearVelocity = velocity * 0.8f;
                bikeBody.angularVelocity = UnityEngine.Random.insideUnitSphere * 4f;
            }

            Crashed?.Invoke(velocity);
        }

        private void Respawn()
        {
            (Vector3 position, Vector3 heading) = PickRespawnPoint();

            _ragdoll?.Destroy();
            _ragdoll = null;

            if (Bike)
            {
                if (Bike.TryGetComponent(out Rigidbody bikeBody)) DestroyImmediate(bikeBody);
                if (Bike.TryGetComponent(out BoxCollider box)) DestroyImmediate(box);
            }

            _motor.SetFrozen(false);
            _motor.Teleport(position + Vector3.up * 0.2f, heading);
            _driver.Following = true;

            if (_rider == null && Bike)
                _rider = new RiderBody(Bike);

            _safePoints.Clear();
            _safePointTimer = 0f;
            IsCrashed = false;
            Respawned?.Invoke();
        }

        private (Vector3, Vector3) PickRespawnPoint()
        {
            float before = (IsCrashed ? _crashTime : Time.time) - RespawnRewindSeconds;
            for (int i = _safePoints.Count - 1; i >= 0; i--)
                if (_safePoints[i].time <= before)
                    return (_safePoints[i].position, _safePoints[i].heading);

            return _safePoints.Count > 0
                ? (_safePoints[0].position, _safePoints[0].heading)
                : (_startPosition, _startHeading);
        }
    }
}
