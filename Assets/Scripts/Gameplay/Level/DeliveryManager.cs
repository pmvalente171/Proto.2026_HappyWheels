using Gameplay.Juice;
using Gameplay.Player;
using UnityEngine;

namespace Gameplay.Level
{
    /// <summary>
    /// The delivery loop: ride to the pickup marker to grab a package, then deliver it to the drop-off marker before
    /// the time runs out. Pay depends on distance and time left. Crashing while carrying it sends the package flying
    /// and fails the delivery. Uses the sidewalk spots from the <see cref="StreetLevelGenerator"/> on the same object.
    /// Draws its own simple HUD at the top of the screen.
    /// </summary>
    [RequireComponent(typeof(StreetLevelGenerator))]
    public class DeliveryManager : MonoBehaviour
    {
        [Tooltip("Average speed the time limit assumes, in km/h. Lower = more time per delivery. " +
                 "Reference: relaxed riding ≈ 15, fast ≈ 25, flat out with no crashes ≈ 35.")]
        [SerializeField, Min(1f)] private float expectedSpeedKmh = 20f;

        [Tooltip("Extra seconds added to every delivery's time limit (for corners, traffic and mistakes).")]
        [SerializeField, Min(0f)] private float extraSeconds = 15f;

        [Tooltip("Shortest delivery, in metres in a straight line from pickup to drop-off.")]
        [SerializeField, Min(10f)] private float minDeliveryMetres = 120f;

        [Tooltip("Longest delivery, in metres in a straight line from pickup to drop-off.")]
        [SerializeField, Min(10f)] private float maxDeliveryMetres = 350f;

        private const float ReachRadius = 3.5f;          // how close counts as arriving (m)
        private const float MessageSeconds = 2.5f;
        private const float BasePay = 5f;                // $ per delivery...
        private const float PayPerMetre = 0.05f;         // ...plus $1 per 20 m...
        private const float PayPerSecondLeft = 1f;       // ...plus $1 per second left

        private static readonly Color PickupColor = new(0.2f, 0.8f, 1f);
        private static readonly Color DropColor = new(1f, 0.75f, 0.1f);
        private static readonly Color PackageColor = new(0.65f, 0.45f, 0.25f);

        private enum State { ToPickup, Delivering }

        private StreetLevelGenerator _city;
        private BallMotor _player;
        private CrashController _crash;

        private State _state;
        private Vector3 _pickup, _drop;
        private float _timeLeft, _deliveryDistance;
        private int _money, _delivered;
        private string _message;
        private float _messageUntil;
        private bool _jobLost;

        private GameObject _marker;
        private Transform _ring, _icon;
        private GameObject _package;

        // HUD juice
        private float _shownMoney;      // rolls up towards _money
        private float _messageStart;
        private int _lastCountdownSecond;

        private Vector3 Target => _state == State.ToPickup ? _pickup : _drop;

        private void Awake() => _city = GetComponent<StreetLevelGenerator>();

        private void Start()
        {
            _player = FindAnyObjectByType<BallMotor>();
            _crash = _player ? _player.GetComponent<CrashController>() : null;
            if (!_player || _city.DeliverySpots.Count < 2)
            {
                Debug.LogError($"{nameof(DeliveryManager)}: needs a {nameof(BallMotor)} in the scene and a generated city.", this);
                enabled = false;
                return;
            }

            if (_crash)
            {
                _crash.Crashed += OnCrashed;
                _crash.Respawned += OnRespawned;
            }

            _marker = BuildMarker();
            NewJob();
        }

        private void OnDestroy()
        {
            if (_crash)
            {
                _crash.Crashed -= OnCrashed;
                _crash.Respawned -= OnRespawned;
            }
        }

        private void Update()
        {
            AnimateMarker();
            _shownMoney = Mathf.MoveTowards(_shownMoney, _money, Mathf.Max(20f, Mathf.Abs(_money - _shownMoney) * 3f) * Time.unscaledDeltaTime);

            if (_crash && _crash.IsCrashed)
                return;

            if (_state == State.Delivering)
            {
                _timeLeft -= Time.deltaTime;

                // Ticking countdown for the last 5 seconds
                int second = Mathf.CeilToInt(_timeLeft);
                if (second <= 5 && second != _lastCountdownSecond && second > 0)
                    JuiceAudio.Play(JuiceAudio.Bonk, 0.5f, 1.6f);
                _lastCountdownSecond = second;

                if (_timeLeft <= 0f)
                {
                    DropPackage(throwIt: false);
                    Show("Too late! Delivery failed.");
                    JuiceAudio.Play(JuiceAudio.Fail, 0.6f);
                    NewJob();
                    return;
                }
            }

            if (FlatDistance(_player.transform.position, Target) > ReachRadius)
                return;

            if (_state == State.ToPickup)
            {
                _state = State.Delivering;
                _deliveryDistance = FlatDistance(_pickup, _drop);
                _timeLeft = _deliveryDistance / (expectedSpeedKmh / 3.6f) + extraSeconds;
                AttachPackage();
                JuiceAudio.Play(JuiceAudio.Pickup, 0.7f);
                JuiceFx.Burst(_pickup + Vector3.up, PickupColor, 30, 5f, 0.35f, 0.8f, 0.5f);
                CameraJuice.Shake(0.1f);
                PlaceMarker();
                Show("Got the package! Deliver it before the time runs out.");
            }
            else
            {
                int pay = Mathf.RoundToInt(BasePay + _deliveryDistance * PayPerMetre + _timeLeft * PayPerSecondLeft);
                _money += pay;
                _delivered++;
                DropPackage(throwIt: false);
                JuiceAudio.Play(JuiceAudio.Ding, 0.8f);
                JuiceFx.Burst(_drop + Vector3.up, Color.white, 80, 9f, 0.3f, 2f, 0.6f, JuiceFx.Confetti());
                CameraJuice.Shake(0.15f);
                Show($"Delivered! +${pay}");
                NewJob();
            }
        }

        #region Jobs

        private void NewJob()
        {
            _state = State.ToPickup;
            Vector3 from = _player.transform.position;
            _pickup = RandomSpot(from, 30f, 150f);
            _drop = RandomSpot(_pickup, minDeliveryMetres, maxDeliveryMetres);
            PlaceMarker();
        }

        /// <summary>A delivery spot between <paramref name="min"/> and <paramref name="max"/> metres from <paramref name="from"/>, or the closest match.</summary>
        private Vector3 RandomSpot(Vector3 from, float min, float max)
        {
            var spots = _city.DeliverySpots;
            int start = Random.Range(0, spots.Count);
            Vector3 best = spots[start];
            float bestError = float.MaxValue;

            for (int n = 0; n < spots.Count; n++)
            {
                Vector3 spot = spots[(start + n) % spots.Count];
                float d = FlatDistance(from, spot);
                if (d >= min && d <= max)
                    return spot;

                float error = d < min ? min - d : d - max;
                if (error < bestError)
                {
                    bestError = error;
                    best = spot;
                }
            }
            return best;
        }

        private void OnCrashed(Vector3 velocity)
        {
            if (_state != State.Delivering)
                return;

            DropPackage(throwIt: true, velocity);
            Show("Package lost!");
            JuiceAudio.Play(JuiceAudio.Fail, 0.5f);
            _state = State.ToPickup;
            _jobLost = true; // a new job is handed out on respawn
        }

        private void OnRespawned()
        {
            if (_jobLost)
            {
                _jobLost = false;
                NewJob();
            }
        }

        #endregion

        #region Package and marker

        private void AttachPackage()
        {
            Transform bike = _crash ? _crash.Bike : null;
            _package = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _package.name = "Package";
            _package.GetComponent<Renderer>().sharedMaterial = ColorMaterials.Lit(PackageColor);
            _package.GetComponent<Collider>().enabled = false;
            _package.transform.localScale = new Vector3(0.45f, 0.35f, 0.45f);

            if (bike)
            {
                // On the rear rack
                _package.transform.SetParent(bike, false);
                _package.transform.localPosition = new Vector3(0f, 1f, -0.75f);
            }
            else
            {
                _package.transform.SetParent(_player.transform, false);
                _package.transform.localPosition = Vector3.up;
            }
        }

        private void DropPackage(bool throwIt, Vector3 velocity = default)
        {
            if (!_package)
                return;

            if (throwIt)
            {
                _package.transform.SetParent(null, true);
                _package.GetComponent<Collider>().enabled = true;
                var body = _package.AddComponent<Rigidbody>();
                body.mass = 3f;
                body.linearVelocity = velocity + Vector3.up * 4f + Random.insideUnitSphere * 2f;
                body.angularVelocity = Random.insideUnitSphere * 8f;
                Destroy(_package, 10f);
            }
            else
            {
                Destroy(_package);
            }
            _package = null;
        }

        /// <summary>A tall beam (visible from far away) over a ring on the ground. No colliders.</summary>
        /// <summary>Pulses the ring, and spins and bobs the floating package over the pickup.</summary>
        private void AnimateMarker()
        {
            if (!_marker)
                return;

            float t = Time.time;
            float pulse = 1f + Mathf.Sin(t * 4f) * 0.08f;
            _ring.localScale = new Vector3(ReachRadius * 2f * pulse, 0.02f, ReachRadius * 2f * pulse);

            _icon.gameObject.SetActive(_state == State.ToPickup);
            _icon.localPosition = Vector3.up * (2.2f + Mathf.Sin(t * 2.5f) * 0.25f);
            _icon.localRotation = Quaternion.Euler(15f, t * 90f, 0f);
        }

        private GameObject BuildMarker()
        {
            var marker = new GameObject("Delivery Marker");

            // Floating package over the pickup (keeps its own colour)
            GameObject icon = GameObject.CreatePrimitive(PrimitiveType.Cube);
            icon.name = "Package Icon";
            DestroyImmediate(icon.GetComponent<Collider>());
            icon.transform.SetParent(marker.transform, false);
            icon.transform.localScale = new Vector3(0.7f, 0.55f, 0.7f);
            _icon = icon.transform;

            GameObject beam = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            beam.name = "Beam";
            DestroyImmediate(beam.GetComponent<Collider>());
            beam.transform.SetParent(marker.transform, false);
            beam.transform.localPosition = Vector3.up * 30f;
            beam.transform.localScale = new Vector3(0.5f, 30f, 0.5f);

            GameObject ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "Ring";
            DestroyImmediate(ring.GetComponent<Collider>());
            ring.transform.SetParent(marker.transform, false);
            ring.transform.localPosition = Vector3.up * 0.03f;
            ring.transform.localScale = new Vector3(ReachRadius * 2f, 0.02f, ReachRadius * 2f);
            _ring = ring.transform;

            return marker;
        }

        private void PlaceMarker()
        {
            _marker.transform.position = Target;
            Material material = ColorMaterials.Unlit(_state == State.ToPickup ? PickupColor : DropColor);
            foreach (Renderer r in _marker.GetComponentsInChildren<Renderer>(true))
                r.sharedMaterial = r.transform == _icon ? ColorMaterials.Lit(PackageColor) : material;
        }

        #endregion

        #region HUD

        private void Show(string message)
        {
            _message = message;
            _messageStart = Time.unscaledTime;
            _messageUntil = _messageStart + MessageSeconds;
        }

        private void OnGUI()
        {
            if (!_player)
                return;

            var big = Style(22, TextAnchor.UpperCenter);
            var small = Style(16, TextAnchor.UpperCenter);
            float w = Screen.width;

            // Money rolls up after a delivery, and glows green while it does
            bool earning = _shownMoney < _money - 0.5f;
            GUIStyle moneyStyle = Style(earning ? 26 : 22, TextAnchor.UpperCenter);
            if (earning) moneyStyle.normal.textColor = new Color(0.5f, 1f, 0.5f);
            GUI.Label(new Rect(0f, 10f, w, 34f), $"${Mathf.RoundToInt(_shownMoney)}    ·    {_delivered} delivered", moneyStyle);

            string task = _state == State.ToPickup
                ? "Pick up the package (blue marker)"
                : $"Deliver the package (yellow marker)    {Mathf.CeilToInt(_timeLeft)} s";
            GUIStyle taskStyle = small;
            if (_state == State.Delivering && _timeLeft < 10f && !(_crash && _crash.IsCrashed))
            {
                // Hurry: red and pulsing
                float beat = Mathf.Abs(Mathf.Sin(Time.unscaledTime * Mathf.PI * 2f));
                taskStyle = Style(Mathf.RoundToInt(16 + beat * 6), TextAnchor.UpperCenter);
                taskStyle.normal.textColor = Color.Lerp(Color.white, new Color(1f, 0.25f, 0.2f), 0.5f + beat * 0.5f);
            }
            if (_crash && _crash.IsCrashed)
                task = "Ouch!  (R to get back on the bike)";
            GUI.Label(new Rect(0f, 44f, w, 28f), task, taskStyle);

            // Compass arrow pointing at the target, relative to where the bike is heading
            Vector3 toTarget = Target - _player.transform.position;
            toTarget.y = 0f;
            Vector3 heading = _player.Heading;
            heading.y = 0f;
            float angle = Vector3.SignedAngle(heading, toTarget, Vector3.up);
            var arrowRect = new Rect(w / 2f - 20f, 70f, 40f, 40f);

            Matrix4x4 previous = GUI.matrix;
            GUIUtility.RotateAroundPivot(angle, arrowRect.center);
            GUI.Label(arrowRect, "▲", Style(30, TextAnchor.MiddleCenter));
            GUI.matrix = previous;
            GUI.Label(new Rect(0f, 110f, w, 24f), $"{toTarget.magnitude:0} m", small);

            // Messages punch in big, settle, then fade out
            if (Time.unscaledTime < _messageUntil && !string.IsNullOrEmpty(_message))
            {
                float age = Time.unscaledTime - _messageStart;
                float punch = 1f + Mathf.Max(0f, 0.6f * (1f - age / 0.25f));
                float alpha = Mathf.Clamp01((_messageUntil - Time.unscaledTime) / 0.5f);
                GUIStyle messageStyle = Style(Mathf.RoundToInt(28 * punch), TextAnchor.MiddleCenter);
                messageStyle.normal.textColor = new Color(1f, 1f, 1f, alpha);
                GUI.Label(new Rect(0f, Screen.height * 0.3f - 30f, w, 60f), _message, messageStyle);
            }
        }

        private static GUIStyle Style(int size, TextAnchor anchor)
        {
            var style = new GUIStyle(GUI.skin.label) { fontSize = size, alignment = anchor, fontStyle = FontStyle.Bold };
            style.normal.textColor = Color.white;
            return style;
        }

        #endregion

        private static float FlatDistance(Vector3 a, Vector3 b)
        {
            a.y = b.y = 0f;
            return Vector3.Distance(a, b);
        }
    }
}
