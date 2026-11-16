using Gameplay.Vehicles;
using UnityEngine;

namespace Gameplay.Player
{
    /// <summary>
    /// Spawns an independent (unparented) <see cref="BicycleRig"/> and drives its pose from the <see cref="BallMotor"/>.
    /// The rig never reads from the player; this component pushes data into it.
    /// </summary>
    [RequireComponent(typeof(BallMotor))]
    public class BicycleDriver : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Must have a BicycleRig component on its root.")]
        [SerializeField] private GameObject rigPrefab;
        [Tooltip("Optional: an existing BicycleRig in the scene. If empty, one is spawned from the prefab.")]
        [SerializeField] private BicycleRig rig;

        [Header("Smoothing")]
        [Tooltip("How quickly the bicycle tilts to match the ground normal (higher = snappier).")]
        [SerializeField] private float tiltSharpness = 12f;

        [Tooltip("Most the bike leans into a turn, in degrees. The lean itself comes from speed and how tight the turn is, like a real bike.")]
        [SerializeField, Range(0f, 60f)] private float maxLeanDegrees = 35f;

        private const float LeanSharpness = 8f;

        private BallMotor _motor;
        private bool _ownsRig;
        private Vector3 _up = Vector3.up;
        private Vector3 _lastForward = Vector3.forward;
        private float _lean; // degrees, positive = leaning right

        public BicycleRig Rig => rig;

        private void Awake()
        {
            _motor = GetComponent<BallMotor>();

            if (!rig && !SpawnRig())
                enabled = false;
        }

        private void Start()
        {
            // In Start, not Awake, so the motor has finished its own setup (radius, initial heading)
            UpdatePose(0f, snap: true);
        }

        private bool SpawnRig()
        {
            if (!rigPrefab)
            {
                Debug.LogError($"{nameof(BicycleDriver)} on '{name}' has no rig prefab assigned.", this);
                return false;
            }

            // No parent: the bike must not inherit the sphere's transform
            GameObject instance = Instantiate(rigPrefab, transform.position, Quaternion.identity);
            instance.name = rigPrefab.name;

            if (!instance.TryGetComponent(out rig))
            {
                Debug.LogError($"Prefab '{rigPrefab.name}' has no {nameof(BicycleRig)} component on its root.", this);
                Destroy(instance);
                return false;
            }

            _ownsRig = true;
            return true;
        }

        private void OnEnable()
        {
            if (rig) rig.gameObject.SetActive(true);
        }

        private void OnDisable()
        {
            if (rig) rig.gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            if (_ownsRig && rig)
                Destroy(rig.gameObject);
        }

        /// <summary>
        /// False lets something else move the rig (e.g. physics after a crash). Setting it back to true snaps the
        /// rig onto the ball again.
        /// </summary>
        public bool Following
        {
            get => _following;
            set
            {
                if (value && !_following && rig)
                    UpdatePose(0f, snap: true);
                _following = value;
            }
        }
        private bool _following = true;

        private void LateUpdate()
        {
            if (_following)
                UpdatePose(Time.deltaTime, snap: false);
        }

        private void UpdatePose(float deltaTime, bool snap)
        {
            // Read the transform (interpolated when the Rigidbody uses Interpolate), not the Rigidbody position
            Vector3 center = transform.position;
            bool grounded = _motor.TryGetGround(center, out RaycastHit hit);

            // Airborne: place the bike where the contact point would be
            Vector3 groundPoint = grounded ? hit.point : center + Vector3.down * _motor.Radius;

            // Tilt toward the ground normal; drift back to world up while airborne
            Vector3 targetUp = grounded ? hit.normal : Vector3.up;
            _up = snap ? targetUp : Vector3.Slerp(_up, targetUp, Damp(tiltSharpness, deltaTime)).normalized;

            // The motor owns the heading; the rig just follows it
            Vector3 forward = Vector3.ProjectOnPlane(_motor.InterpolatedHeading, _up);
            if (forward.sqrMagnitude < 0.0001f)
                forward = Vector3.ProjectOnPlane(Vector3.forward, _up); // heading parallel to up; pick anything sane
            forward.Normalize();

            // Lean into turns: a real bike leans so gravity balances the cornering force, angle = atan(v × turn rate / g)
            float yawRate = deltaTime > 0f ? Vector3.SignedAngle(_lastForward, forward, _up) * Mathf.Deg2Rad / deltaTime : 0f;
            _lastForward = forward;
            float targetLean = grounded
                ? Mathf.Clamp(Mathf.Atan(_motor.ForwardSpeed * yawRate / Physics.gravity.magnitude) * Mathf.Rad2Deg, -maxLeanDegrees, maxLeanDegrees)
                : 0f;
            _lean = snap ? targetLean : Mathf.Lerp(_lean, targetLean, Damp(LeanSharpness, deltaTime));

            // Move the rig, never this transform (the physics sphere). Negative Z roll = lean right.
            rig.SetPose(groundPoint, Quaternion.LookRotation(forward, _up) * Quaternion.Euler(0f, 0f, -_lean), grounded, _motor.ForwardSpeed);
        }

        /// <summary>Frame-rate independent smoothing factor.</summary>
        private static float Damp(float sharpness, float deltaTime) => 1f - Mathf.Exp(-sharpness * deltaTime);
    }
}
