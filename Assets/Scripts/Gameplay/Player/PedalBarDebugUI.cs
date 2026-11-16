using UnityEngine;

namespace Gameplay.Player
{
    /// <summary>
    /// Throwaway on-screen display of the pedal bar, gear and speed (IMGUI, no assets needed).
    /// Meant to be replaced by the real UI later. Put it on the same GameObject as the BallMotor.
    /// </summary>
    [RequireComponent(typeof(BallMotor))]
    public class PedalBarDebugUI : MonoBehaviour
    {
        [Tooltip("Bar width in pixels for the highest gear; lower gears are proportionally shorter.")]
        [SerializeField, Min(50f)] private float maxBarWidth = 500f;
        [SerializeField, Min(4f)] private float barHeight = 24f;
        [Tooltip("Distance from the bottom of the screen, in pixels.")]
        [SerializeField, Min(0f)] private float bottomMargin = 60f;

        private BallMotor _motor;
        private float _lastSpeedKmh;
        private float _slowdown; // km/h lost per second, smoothed
        private float _lastPhase;
        private float _flash;    // 1 right after a completed rotation, fading to 0

        private void Awake() => _motor = GetComponent<BallMotor>();

        // Measures how fast the bike is actually slowing down, to compare with Coasting Km/h Per Second
        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            float speed = _motor.ForwardSpeed * 3.6f;
            float slowdown = (_lastSpeedKmh - speed) / dt;
            _lastSpeedKmh = speed;
            _slowdown = Mathf.Lerp(_slowdown, slowdown, 1f - Mathf.Exp(-dt / 0.5f)); // ~0.5 s smoothing
        }

        private void OnGUI()
        {
            if (_motor.Drivetrain == null)
                return;

            Drivetrain d = _motor.Drivetrain;

            // Flash and pop the bar each time a rotation completes (the crank wraps from ~1 back to ~0)
            if (Event.current.type == EventType.Repaint)
            {
                if (d.CrankPhase < _lastPhase - 0.5f)
                    _flash = 1f;
                _lastPhase = d.CrankPhase;
                _flash = Mathf.Max(0f, _flash - Time.unscaledDeltaTime * 4f);
            }

            float pop = 1f + _flash * 0.15f;
            float width = maxBarWidth * d.BarLength * pop;
            float height = barHeight * pop;
            float x = (Screen.width - width) * 0.5f;
            float y = Screen.height - bottomMargin - barHeight - (height - barHeight) * 0.5f;
            var bar = new Rect(x, y, width, height);

            // Track
            DrawRect(bar, Color.Lerp(new Color(0f, 0f, 0f, 0.6f), new Color(0.3f, 1f, 0.4f, 0.8f), _flash));
            y += (height - barHeight) * 0.5f; // everything else stays its normal height, centred on the popped track

            // Switch windows at both ends: where either key pushes. Brighter while the marker is inside one.
            float zone = width * d.SwitchWindowBarFraction;
            var zoneColor = new Color(1f, 1f, 0f, d.InSwitchWindow ? 0.45f : 0.2f);
            DrawRect(new Rect(x, y, zone, barHeight), zoneColor);
            DrawRect(new Rect(x + width - zone, y, zone, barHeight), zoneColor);

            // Marker: the pedal position, green while pushing
            float markerWidth = 10f;
            float t = (d.BarPosition + 1f) * 0.5f;
            var marker = new Rect(x + t * (width - markerWidth), y, markerWidth, barHeight);
            DrawRect(marker, d.IsPushing ? Color.green : Color.white);

            // Stamina: thin bar under the pedal bar, at the highest gear's width; red when tired, orange while recovering
            Stamina s = _motor.Stamina;
            var staminaBack = new Rect((Screen.width - maxBarWidth) * 0.5f, y + barHeight + 6f, maxBarWidth, 8f);
            DrawRect(staminaBack, new Color(0f, 0f, 0f, 0.6f));
            DrawRect(new Rect(staminaBack.x, staminaBack.y, staminaBack.width * s.Normalized, staminaBack.height),
                s.IsRecovering ? new Color(1f, 0.55f, 0f) : s.IsTired ? Color.red : new Color(0.3f, 0.7f, 1f));

            // Key hints at each end; the key to press now is highlighted
            bool left = d.RequiredPedal == Drivetrain.Pedal.Left;
            DrawLabel(new Rect(x - 30f, y, 25f, barHeight), "Q", left ? Color.yellow : Color.gray);
            DrawLabel(new Rect(x + width + 5f, y, 25f, barHeight), "E", left ? Color.gray : Color.yellow);

            // Stats
            string stats = $"Gear {d.Gear + 1}/{d.GearCount}   " +
                           $"{_motor.ForwardSpeed * 3.6f:0} km/h   " +
                           $"{d.Cadence * 60f:0} rpm" +
                           (!d.IsPushing && !d.IsBoosting && _motor.IsGrounded && Mathf.Abs(_motor.ForwardSpeed) > 0.3f
                               ? $"   coasting {-_slowdown:+0.0;-0.0} km/h/s" : "") +
                           (_motor.BrakeInput > 0f ? "   BRAKE" : "") +
                           (_motor.IsSkidding ? "   SKID" : "") + (d.IsBoosting ? "   BOOST" : "") + (s.IsRecovering ? "   RECOVERING" : "");
            DrawLabel(new Rect(0f, y - 26f, Screen.width, 22f), stats, Color.white, TextAnchor.MiddleCenter);
        }

        private static void DrawRect(Rect rect, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        private static void DrawLabel(Rect rect, string text, Color color, TextAnchor anchor = TextAnchor.MiddleLeft)
        {
            var style = new GUIStyle(GUI.skin.label) { alignment = anchor, fontStyle = FontStyle.Bold, fontSize = 16 };
            style.normal.textColor = color;
            GUI.Label(rect, text, style);
        }
    }
}
