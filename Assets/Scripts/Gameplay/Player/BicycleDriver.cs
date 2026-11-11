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

        private BallMotor _motor;
        private bool _ownsRig;
        private Vector3 _up = Vector3.up;

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

        private void LateUpdate()
        {
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

            // Move the rig, never this transform (the physics sphere)
            rig.SetPose(groundPoint, Quaternion.LookRotation(forward, _up), grounded, _motor.ForwardSpeed);
        }

        /// <summary>Frame-rate independent smoothing factor.</summary>
        private static float Damp(float sharpness, float deltaTime) => 1f - Mathf.Exp(-sharpness * deltaTime);
    }
}
