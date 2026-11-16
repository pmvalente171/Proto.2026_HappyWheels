using UnityEngine;
using UnityEngine.Rendering;

namespace Gameplay.Juice
{
    /// <summary>
    /// Screen shake and a field-of-view kick for the main camera. Applied just before the camera renders and undone
    /// right after, so it works on top of whatever moves the camera (Cinemachine) without fighting it.
    /// Added to the main camera automatically the first time it's used.
    /// </summary>
    public class CameraJuice : MonoBehaviour
    {
        private const float MaxOffset = 0.35f;     // metres at full shake
        private const float MaxAngle = 4f;         // degrees at full shake
        private const float TraumaDecay = 1.4f;    // per second
        private const float FovSharpness = 4f;

        private static CameraJuice _instance;

        private Camera _camera;
        private float _trauma;
        private float _fovKick, _fovKickTarget;
        private bool _applied;
        private Vector3 _savedPosition;
        private Quaternion _savedRotation;
        private float _savedFov;

        private static CameraJuice Instance
        {
            get
            {
                if (_instance)
                    return _instance;
                Camera main = Camera.main;
                if (!main)
                    return null;
                _instance = main.GetComponent<CameraJuice>();
                if (!_instance)
                    _instance = main.gameObject.AddComponent<CameraJuice>();
                return _instance;
            }
        }

        /// <summary>Adds screen shake. 0.1 = a bump, 0.4 = a hard landing, 1 = a crash. Shakes add up, to a maximum of 1.</summary>
        public static void Shake(float amount)
        {
            CameraJuice juice = Instance;
            if (juice)
                juice._trauma = Mathf.Clamp01(juice._trauma + amount);
        }

        /// <summary>Extra field of view in degrees (eased towards), e.g. more at high speed.</summary>
        public static void SetFovKick(float degrees)
        {
            CameraJuice juice = Instance;
            if (juice)
                juice._fovKickTarget = degrees;
        }

        private void Awake() => _camera = GetComponent<Camera>();

        private void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
            RenderPipelineManager.endCameraRendering += OnEndCamera;
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            RenderPipelineManager.endCameraRendering -= OnEndCamera;
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime; // keeps working during slow motion
            _trauma = Mathf.Max(0f, _trauma - TraumaDecay * dt);
            _fovKick = Mathf.Lerp(_fovKick, _fovKickTarget, 1f - Mathf.Exp(-FovSharpness * dt));
        }

        private void OnBeginCamera(ScriptableRenderContext context, Camera cam)
        {
            if (cam != _camera || _applied)
                return;

            Transform t = _camera.transform;
            _savedPosition = t.position;
            _savedRotation = t.rotation;
            _savedFov = _camera.fieldOfView;
            _applied = true;

            // Squared, so small shakes stay subtle and big ones are violent
            float shake = _trauma * _trauma;
            float time = Time.unscaledTime * 25f;
            if (shake > 0f)
            {
                t.position += (t.right * Noise(time, 0f) + t.up * Noise(time, 10f)) * MaxOffset * shake;
                t.rotation *= Quaternion.Euler(Noise(time, 20f) * MaxAngle * shake, Noise(time, 30f) * MaxAngle * shake,
                    Noise(time, 40f) * MaxAngle * 1.5f * shake);
            }
            _camera.fieldOfView = _savedFov + _fovKick;
        }

        private void OnEndCamera(ScriptableRenderContext context, Camera cam)
        {
            if (cam != _camera || !_applied)
                return;

            _camera.transform.SetPositionAndRotation(_savedPosition, _savedRotation);
            _camera.fieldOfView = _savedFov;
            _applied = false;
        }

        private static float Noise(float time, float seed) => Mathf.PerlinNoise(time, seed) * 2f - 1f;
    }
}
