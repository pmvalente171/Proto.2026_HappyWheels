using UnityEngine;

namespace Gameplay.Player
{
    /// <summary>
    /// Bicycle physics on a rolling sphere. The sphere acts as the wheel: it rolls with real PhysX friction, but it
    /// may only spin around its axle (perpendicular to the heading). Steering turns the axle, and contact friction
    /// drags the ball into the turn like a tire. Grip therefore comes from the physics materials (player × ground).
    /// Input comes from the Bike action map: alternate Q/E to pedal (see <see cref="Drivetrain"/>),
    /// arrows to steer, W/S to change gear, Space to brake.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class BallMotor : MonoBehaviour
    {
        // High enough to never limit speed (Unity's default of 7 rad/s caps a 0.5 m ball at ~3.5 m/s)
        private const float MaxSpin = 200f;

        [Header("References")]
        [SerializeField] private InputBootstrap input;

        [Header("Ground")]
        [Tooltip("Layers considered ground. Must NOT include the player or bicycle layers.")]
        [SerializeField] private LayerMask groundLayers = ~0;
        [Tooltip("Extra distance below the sphere's surface that still counts as grounded.")]
        [SerializeField, Min(0f)] private float groundCheckDistance = 0.1f;

        [Header("Tuning")]
        [Tooltip("Steering, pedalling, stamina and braking values. Edit them live in Window > Tuning.")]
        [SerializeField] private BikeConfig config;

        private Drivetrain _drivetrain;
        private Stamina _stamina;
        private Rigidbody _rigidbody;
        private float _castRadius;

        private Vector3 _heading = Vector3.forward;
        private Vector3 _previousHeading = Vector3.forward;

        /// <summary>Steering input, -1 (left) to +1 (right).</summary>
        public float SteerInput { get; private set; }
        /// <summary>Brake input, 0 to 1.</summary>
        public float BrakeInput { get; private set; }
        public bool LeftPedalHeld { get; private set; }
        public bool RightPedalHeld { get; private set; }

        /// <summary>Pedals, crank bar and gears.</summary>
        public Drivetrain Drivetrain => _drivetrain;
        /// <summary>Rider stamina, drained by pedalling.</summary>
        public Stamina Stamina => _stamina;

        /// <summary>Direction the bike points, as of the last physics step.</summary>
        public Vector3 Heading => _heading;

        /// <summary>Heading interpolated between physics steps, for smooth visuals (matches Rigidbody interpolation).</summary>
        public Vector3 InterpolatedHeading
        {
            get
            {
                float t = Mathf.Clamp01((Time.time - Time.fixedTime) / Time.fixedDeltaTime);
                return Vector3.Slerp(_previousHeading, _heading, t);
            }
        }

        /// <summary>Speed of the ball along the heading (m/s); negative when rolling backwards.</summary>
        public float ForwardSpeed { get; private set; }
        /// <summary>Surface speed of the wheel (m/s). Differs from ForwardSpeed while skidding or with a locked wheel.</summary>
        public float WheelSpeed { get; private set; }
        /// <summary>Current handlebar angle in degrees; positive = right.</summary>
        public float SteerAngle { get; private set; }
        public bool IsGrounded { get; private set; }
        /// <summary>True when the contact patch slides faster than Skid Slip Speed (cornering too hard, locked brake...).</summary>
        public bool IsSkidding { get; private set; }
        public Vector3 GroundNormal { get; private set; } = Vector3.up;
        /// <summary>World radius of the sphere collider.</summary>
        public float Radius { get; private set; } = 0.5f;

        private void Awake()
        {
            _rigidbody = GetComponent<Rigidbody>();
            _rigidbody.maxAngularVelocity = MaxSpin;

            Radius = ComputeRadius();
            _castRadius = Radius * 0.95f;

            _heading = _previousHeading = FlattenHeading(transform.forward, Vector3.up);

            if (!config)
            {
                Debug.LogError($"{nameof(BallMotor)} on '{name}' has no {nameof(BikeConfig)} assigned.", this);
                enabled = false;
                return;
            }

            // Both read the config's settings live, so edits during play take effect immediately
            _drivetrain = new Drivetrain(config.drivetrain);
            _stamina = new Stamina(config.stamina);

            if (!input)
            {
                Debug.LogError($"{nameof(BallMotor)} on '{name}' has no {nameof(InputBootstrap)} assigned.", this);
                enabled = false;
            }
        }

        private void Update()
        {
            var bike = input.Actions.Bike;
            SteerInput = Mathf.Clamp(bike.Steer.ReadValue<float>(), -1f, 1f);
            BrakeInput = Mathf.Clamp01(bike.Brake.ReadValue<float>());
            LeftPedalHeld = bike.PedalLeft.IsPressed();
            RightPedalHeld = bike.PedalRight.IsPressed();

            // Gears change once per press, so they're handled here rather than in FixedUpdate
            if (bike.GearUp.WasPressedThisFrame()) _drivetrain.ShiftUp();
            if (bike.GearDown.WasPressedThisFrame()) _drivetrain.ShiftDown();
        }

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            _previousHeading = _heading;

            IsGrounded = TryGetGround(_rigidbody.position, out RaycastHit hit);
            GroundNormal = IsGrounded ? hit.normal : Vector3.up;
            Vector3 up = GroundNormal;

            Vector3 forward = FlattenHeading(_heading, up);
            Vector3 velocity = _rigidbody.linearVelocity;

            // The handlebar turns at a limited rate, even in the air
            float targetSteer = SteerInput * config.maxSteerAngle.Evaluate(Mathf.Abs(Vector3.Dot(velocity, forward)));
            SteerAngle = Mathf.MoveTowards(SteerAngle, targetSteer, config.steerRate * dt);

            // No tire contact in the air: the heading only changes on the ground
            if (IsGrounded)
                forward = Steer(forward, up, Vector3.Dot(velocity, forward), dt);
            _heading = forward;
            ForwardSpeed = Vector3.Dot(velocity, forward);

            // The sphere is a wheel: it may only spin around its axle. Removing the other components is what
            // stops it rolling sideways, so friction at the contact patch acts as the tire's sideways grip.
            Vector3 axle = Vector3.Cross(up, forward);
            float spin = Vector3.Dot(_rigidbody.angularVelocity, axle); // rad/s, positive = rolling forward

            float drive = _drivetrain.Step(LeftPedalHeld, RightPedalHeld, spin * Radius, IsGrounded, _stamina.BarSpeedMultiplier, dt);
            _stamina.Tick(_drivetrain.IsPushing, _drivetrain.Effort, dt);
            if (IsGrounded)
                spin = ApplyPedalsAndBrakes(spin, drive);
            _rigidbody.angularVelocity = axle * spin;

            WheelSpeed = spin * Radius;

            // Contact patch velocity = ball velocity minus the wheel's surface velocity
            Vector3 slip = Vector3.ProjectOnPlane(velocity - forward * WheelSpeed, up);
            IsSkidding = IsGrounded && slip.magnitude > config.skidSlipSpeed;
        }

        /// <summary>Rotates the heading using the kinematic bicycle model: yaw rate = v / wheelbase × tan(steer).</summary>
        private Vector3 Steer(Vector3 forward, Vector3 up, float forwardSpeed, float dt)
        {
            float turnSpeed = Mathf.Abs(forwardSpeed) < config.minTurnSpeed
                ? config.minTurnSpeed * Mathf.Sign(forwardSpeed)
                : forwardSpeed;

            float yawRate = turnSpeed / config.wheelbase * Mathf.Tan(SteerAngle * Mathf.Deg2Rad); // rad/s
            return Quaternion.AngleAxis(yawRate * Mathf.Rad2Deg * dt, up) * forward;
        }

        /// <summary>
        /// Changes the wheel's spin; contact friction then turns that into ball acceleration.
        /// Accelerations are given in m/s² of ball speed and converted to the spin change that produces them.
        /// </summary>
        private float ApplyPedalsAndBrakes(float spin, float drive)
        {
            float dt = Time.fixedDeltaTime;

            // A spin change Δω on a rolling ball becomes Δv = Δω·r / k (the rest goes into rotation),
            // with k = 1 + m·r² / I. So to get Δv we need Δω = Δv·k / r.
            float inertia = Mathf.Max(_rigidbody.inertiaTensor.x, 0.0001f);
            float spinPerSpeed = (1f + _rigidbody.mass * Radius * Radius / inertia) / Radius;

            // Pedalling: speed bursts from completed rotations (see Drivetrain)
            spin += drive * spinPerSpeed * dt;

            // Bikes don't reverse: braking and rolling resistance only slow the wheel, to zero at most.
            // A strong brake stops the wheel faster than friction can stop the ball: that's a locked, skidding wheel.
            float resistance = (BrakeInput * config.brakeDeceleration + config.rollingResistance) * spinPerSpeed * dt;
            spin -= Mathf.Sign(spin) * Mathf.Min(resistance, Mathf.Abs(spin));

            return spin;
        }

        /// <summary>
        /// Sphere-casts down from <paramref name="center"/> using this motor's ground settings.
        /// Public so visuals can test from the interpolated position.
        /// </summary>
        public bool TryGetGround(Vector3 center, out RaycastHit hit)
        {
            float distance = groundCheckDistance + (Radius - _castRadius);
            return Physics.SphereCast(center, _castRadius, Vector3.down, out hit, distance,
                groundLayers, QueryTriggerInteraction.Ignore);
        }

        private static Vector3 FlattenHeading(Vector3 heading, Vector3 up)
        {
            Vector3 flat = Vector3.ProjectOnPlane(heading, up);
            if (flat.sqrMagnitude < 0.0001f)
                flat = Vector3.ProjectOnPlane(Vector3.forward, up); // heading parallel to up; pick anything sane
            return flat.normalized;
        }

        private float ComputeRadius()
        {
            if (TryGetComponent(out SphereCollider sphere))
            {
                Vector3 s = transform.lossyScale;
                return sphere.radius * Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
            }

            if (TryGetComponent(out Collider anyCollider))
                return anyCollider.bounds.extents.y;

            return Radius;
        }
    }
}
