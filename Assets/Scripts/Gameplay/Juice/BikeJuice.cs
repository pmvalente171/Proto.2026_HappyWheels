using System.Collections;
using Gameplay.Level;
using Gameplay.Player;
using UnityEngine;

namespace Gameplay.Juice
{
    /// <summary>
    /// Feedback for the bike, all visual or audio (no effect on handling):
    /// spinning wheels with spokes, steering fork, turning cranks and pedals (the rider's legs follow them),
    /// skid marks and smoke, dust and screen shake on impacts, a speed field-of-view kick, slow motion on crashes,
    /// and wind, freewheel and impact sounds.
    /// Expects the default bicycle model (children named Wheel_Front, Wheel_Back, Handle, Front); missing parts are skipped.
    /// </summary>
    [RequireComponent(typeof(BallMotor), typeof(BicycleDriver), typeof(CrashController))]
    public class BikeJuice : MonoBehaviour
    {
        [Tooltip("Extra camera field of view at Fov Kick Speed, in degrees. Makes speed feel faster. 0 = off.")]
        [SerializeField, Range(0f, 30f)] private float fovKickDegrees = 12f;

        [Tooltip("Speed (km/h) at which the full field-of-view kick is reached.")]
        [SerializeField, Min(1f)] private float fovKickSpeedKmh = 50f;

        [Tooltip("How much the screen shakes on impacts and crashes. 0 = off, 1 = normal, 2 = a lot.")]
        [SerializeField, Range(0f, 2f)] private float shakeStrength = 1f;

        [Tooltip("Slow motion for a moment when the rider crashes.")]
        [SerializeField] private bool slowMotionOnCrash = true;

        [Tooltip("Master volume for the bike's sounds.")]
        [SerializeField, Range(0f, 1f)] private float volume = 0.8f;

        // Bicycle model measurements (rig space, metres)
        private const float WheelRadius = 0.35f;
        private const float MaxVisualSteer = 25f;           // handlebar angle at full steering
        private static readonly Vector3 CrankCentre = new(0f, 0.36f, -0.05f);
        private const float CrankLength = 0.17f;
        private const float PedalOffset = 0.15f;            // sideways, from the frame

        private static readonly Color SpokeColor = new(0.85f, 0.85f, 0.85f);
        private static readonly Color CrankColor = new(0.2f, 0.2f, 0.22f);
        private static readonly Color Dust = new(0.75f, 0.7f, 0.6f, 0.8f);
        private static readonly Color Smoke = new(0.85f, 0.85f, 0.85f, 0.6f);

        private BallMotor _motor;
        private CrashController _crash;
        private Rigidbody _body;

        private Transform _fork, _frontSpin, _backSpin, _crank;
        private float _wheelAngle;

        private TrailRenderer _skidMark;
        private ParticleSystem _skidSmoke;
        private AudioSource _wind, _skid;
        private float _tickTimer;

        private float _defaultFixedDeltaTime;
        private Coroutine _slowMotion;

        private void Awake()
        {
            _motor = GetComponent<BallMotor>();
            _crash = GetComponent<CrashController>();
            _body = GetComponent<Rigidbody>();
            _defaultFixedDeltaTime = Time.fixedDeltaTime;
        }

        private void Start()
        {
            if (_crash.Bike)
                BuildBikeParts(_crash.Bike);

            _skidMark = BuildSkidMark();
            _skidSmoke = JuiceFx.CreateEmitter("Skid Smoke", Smoke, 1f, 0.6f, 1.2f, -0.05f);
            _wind = JuiceAudio.CreateLoop(gameObject, JuiceAudio.WindLoop);
            _skid = JuiceAudio.CreateLoop(gameObject, JuiceAudio.SkidLoop);

            _crash.Crashed += OnCrashed;
            _crash.Respawned += OnRespawned;
            _crash.Impact += OnImpact;
            _crash.KnockedObject += OnKnockedObject;
        }

        private void OnDestroy()
        {
            if (_crash)
            {
                _crash.Crashed -= OnCrashed;
                _crash.Respawned -= OnRespawned;
                _crash.Impact -= OnImpact;
                _crash.KnockedObject -= OnKnockedObject;
            }
            RestoreTime();
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            bool riding = !_crash.IsCrashed;
            float speed = riding ? Mathf.Abs(_motor.WheelSpeed) : 0f;

            AnimateBike(dt);

            // Ground contact point, under the ball
            Vector3 contact = transform.position + Vector3.down * (_motor.Radius - 0.02f);

            // Skids: marks on the ground, smoke and hiss
            bool skidding = riding && _motor.IsSkidding && _motor.IsGrounded;
            _skidMark.transform.position = contact;
            _skidMark.emitting = skidding;
            if (skidding)
            {
                _skidSmoke.transform.position = contact;
                _skidSmoke.Emit(1);
            }
            _skid.volume = Mathf.MoveTowards(_skid.volume, skidding ? 0.35f * volume : 0f, dt * 4f);

            // Wind gets louder and higher with speed
            float windAmount = Mathf.Clamp01(speed / 15f);
            _wind.volume = windAmount * windAmount * 0.5f * volume;
            _wind.pitch = 0.7f + windAmount * 0.8f;

            // Freewheel ticking while coasting
            if (riding && _motor.IsGrounded && !_motor.Drivetrain.IsPushing && speed > 0.5f)
            {
                float ticksPerSecond = Mathf.Min(speed / (2f * Mathf.PI * WheelRadius) * 12f, 40f);
                _tickTimer -= dt * ticksPerSecond;
                if (_tickTimer <= 0f)
                {
                    _tickTimer += 1f;
                    JuiceAudio.Play(JuiceAudio.Tick, 0.15f * volume, Random.Range(0.9f, 1.1f));
                }
            }

            CameraJuice.SetFovKick(riding ? fovKickDegrees * Mathf.Clamp01(_body.linearVelocity.magnitude * 3.6f / fovKickSpeedKmh) : 0f);
        }

        #region Bike animation

        private void AnimateBike(float dt)
        {
            if (_crash.IsCrashed)
                return;

            // Wheels roll at the wheel's speed
            _wheelAngle += _motor.WheelSpeed / WheelRadius * Mathf.Rad2Deg * dt;
            Quaternion spin = Quaternion.AngleAxis(_wheelAngle, Vector3.right);
            if (_frontSpin) _frontSpin.localRotation = spin;
            if (_backSpin) _backSpin.localRotation = spin;

            // Handlebars, fork and front wheel turn with the steering
            if (_fork) _fork.localRotation = Quaternion.Euler(0f, _motor.SteerAmount * MaxVisualSteer, 0f);

            // Cranks follow the pedal bar: one bar rotation = one crank rotation
            float crankAngle = _motor.Drivetrain.CrankPhase * 360f;
            Quaternion crankRotation = Quaternion.AngleAxis(crankAngle, Vector3.right);
            if (_crank) _crank.localRotation = crankRotation;

            Vector3 rightPedal = CrankCentre + crankRotation * new Vector3(PedalOffset, 0f, CrankLength);
            Vector3 leftPedal = CrankCentre + crankRotation * new Vector3(-PedalOffset, 0f, -CrankLength);
            float sway = Mathf.Sin(crankAngle * Mathf.Deg2Rad) * (_motor.Drivetrain.IsPushing ? 1f : 0f);
            _crash.Rider?.SetPose(leftPedal + Vector3.up * 0.04f, rightPedal + Vector3.up * 0.04f, sway);
        }

        private void BuildBikeParts(Transform rig)
        {
            Transform front = rig.Find("Front");
            Transform handle = rig.Find("Handle");
            Transform frontWheel = rig.Find("Wheel_Front");
            Transform backWheel = rig.Find("Wheel_Back");

            // Fork: pivot for everything that steers, on the steering axis
            float forkZ = front ? front.localPosition.z : 0.5f;
            _fork = NewPivot("Fork", rig, new Vector3(0f, 0f, forkZ));
            if (front) front.SetParent(_fork, true);
            if (handle) handle.SetParent(_fork, true);

            if (frontWheel) _frontSpin = WheelPivot("Front Wheel Spin", frontWheel, _fork);
            if (backWheel) _backSpin = WheelPivot("Back Wheel Spin", backWheel, rig);

            // Cranks and pedals
            _crank = NewPivot("Crank", rig, CrankCentre);
            for (int side = -1; side <= 1; side += 2)
            {
                Part("Crank Arm", _crank, new Vector3(side * 0.1f, 0f, side * CrankLength / 2f), new Vector3(0.03f, 0.03f, CrankLength), CrankColor);
                Part("Pedal", _crank, new Vector3(side * PedalOffset, 0f, side * CrankLength), new Vector3(0.1f, 0.025f, 0.06f), CrankColor);
            }
        }

        /// <summary>Puts a wheel under a spinning pivot at its centre, with spokes so the spin is visible.</summary>
        private static Transform WheelPivot(string name, Transform wheel, Transform parent)
        {
            Transform pivot = NewPivot(name, parent, parent.InverseTransformPoint(wheel.position));
            wheel.SetParent(pivot, true);

            float length = WheelRadius * 1.85f;
            for (int i = 0; i < 3; i++)
            {
                Transform spoke = Part("Spoke", pivot, Vector3.zero, new Vector3(0.09f, 0.02f, length), SpokeColor).transform;
                spoke.localRotation = Quaternion.AngleAxis(i * 60f, Vector3.right);
            }
            return pivot;
        }

        private static Transform NewPivot(string name, Transform parent, Vector3 localPosition)
        {
            var pivot = new GameObject(name).transform;
            pivot.SetParent(parent, false);
            pivot.localPosition = localPosition;
            return pivot;
        }

        private static GameObject Part(string name, Transform parent, Vector3 localPosition, Vector3 size, Color color)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            DestroyImmediate(go.GetComponent<Collider>()); // visuals must never touch the physics ball
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = ColorMaterials.Lit(color);
            return go;
        }

        private static TrailRenderer BuildSkidMark()
        {
            var go = new GameObject("Skid Mark");
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // lies flat on the ground
            var trail = go.AddComponent<TrailRenderer>();
            trail.alignment = LineAlignment.TransformZ;
            trail.time = 10f;
            trail.minVertexDistance = 0.1f;
            trail.widthMultiplier = 0.18f;
            trail.emitting = false;
            trail.sharedMaterial = ColorMaterials.Unlit(new Color(0.08f, 0.08f, 0.08f));
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return trail;
        }

        #endregion

        #region Events

        private void OnImpact(Vector3 point, float kmh, bool wall)
        {
            float strength = Mathf.Clamp01(kmh / 40f);
            CameraJuice.Shake(strength * 0.6f * shakeStrength);
            JuiceAudio.Play(JuiceAudio.Thud, (0.3f + strength * 0.7f) * volume, Random.Range(0.85f, 1.15f));
            JuiceFx.Burst(point, Dust, Mathf.RoundToInt(6 + strength * 24), 2f + strength * 3f, 0.5f, 0.9f, -0.05f);
        }

        private void OnKnockedObject(Vector3 point, float kmh)
        {
            JuiceAudio.Play(JuiceAudio.Bonk, 0.7f * volume, Random.Range(0.8f, 1.25f));
            CameraJuice.Shake(0.08f * shakeStrength);
            JuiceFx.Burst(point, new Color(1f, 0.5f, 0.1f), 6, 3f, 0.15f, 0.5f, 1f);
        }

        private void OnCrashed(Vector3 velocity)
        {
            CameraJuice.Shake(1f * shakeStrength);
            JuiceAudio.Play(JuiceAudio.Thud, volume, 0.7f);
            JuiceAudio.Play(JuiceAudio.Whoosh, 0.6f * volume, 1.2f);
            JuiceFx.Burst(transform.position, Dust, 40, 5f, 0.8f, 1.2f, -0.05f);
            _skidMark.emitting = false;

            if (slowMotionOnCrash)
            {
                if (_slowMotion != null) StopCoroutine(_slowMotion);
                _slowMotion = StartCoroutine(SlowMotion(0.25f, 0.9f));
            }
        }

        private void OnRespawned()
        {
            RestoreTime();
            JuiceAudio.Play(JuiceAudio.Pickup, 0.4f * volume, 0.8f);
            JuiceFx.Burst(transform.position, Color.white, 25, 3f, 0.6f, 0.7f, -0.2f);
            _skidMark.Clear();
        }

        /// <summary>Drops to <paramref name="scale"/> speed, then eases back to normal over <paramref name="seconds"/> (real time).</summary>
        private IEnumerator SlowMotion(float scale, float seconds)
        {
            for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
            {
                float eased = Mathf.Lerp(scale, 1f, Mathf.SmoothStep(0f, 1f, t / seconds));
                Time.timeScale = eased;
                Time.fixedDeltaTime = _defaultFixedDeltaTime * eased; // keep physics smooth while slowed
                yield return null;
            }
            RestoreTime();
        }

        private void RestoreTime()
        {
            if (_slowMotion != null)
            {
                StopCoroutine(_slowMotion);
                _slowMotion = null;
            }
            Time.timeScale = 1f;
            Time.fixedDeltaTime = _defaultFixedDeltaTime;
        }

        #endregion
    }
}
