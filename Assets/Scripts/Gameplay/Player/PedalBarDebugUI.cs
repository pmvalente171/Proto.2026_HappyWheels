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

        private void Awake() => _motor = GetComponent<BallMotor>();

        private void OnGUI()
        {
            if (_motor.Drivetrain == null)
                return;

            Drivetrain d = _motor.Drivetrain;

            float width = maxBarWidth * d.Development / d.MaxDevelopment;
            float x = (Screen.width - width) * 0.5f;
            float y = Screen.height - bottomMargin - barHeight;
            var bar = new Rect(x, y, width, barHeight);

            // Track
            DrawRect(bar, new Color(0f, 0f, 0f, 0.6f));

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
