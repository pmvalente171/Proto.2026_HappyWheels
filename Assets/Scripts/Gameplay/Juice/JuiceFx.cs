using UnityEngine;
using UnityEngine.Rendering;

namespace Gameplay.Juice
{
    /// <summary>Particle effects built in code (soft round particles, no assets needed).</summary>
    public static class JuiceFx
    {
        private static Material _material;

        /// <summary>Transparent, unlit particle material with a soft round dot texture. Tinted per particle.</summary>
        public static Material ParticleMaterial
        {
            get
            {
                if (_material)
                    return _material;

                Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Particles/Standard Unlit");
                _material = new Material(shader) { name = "Juice Particles" };
                Texture2D dot = SoftDot();
                _material.mainTexture = dot;
                if (_material.HasProperty("_BaseMap")) _material.SetTexture("_BaseMap", dot);

                // URP transparent (alpha blended) setup
                _material.SetFloat("_Surface", 1f);
                _material.SetFloat("_Blend", 0f);
                _material.SetOverrideTag("RenderType", "Transparent");
                _material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                _material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                _material.SetInt("_ZWrite", 0);
                _material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                _material.renderQueue = (int)RenderQueue.Transparent;
                return _material;
            }
        }

        /// <summary>One-off burst of particles. The object removes itself when the particles are gone.</summary>
        /// <param name="gravity">1 = falls like a real object, 0 = floats, negative = rises (smoke).</param>
        public static void Burst(Vector3 position, Color color, int count, float speed, float size, float lifetime,
            float gravity = 0f, Gradient randomColors = null)
        {
            if (count <= 0)
                return;

            ParticleSystem ps = Create("FX Burst", null, color, speed, size, lifetime, gravity, randomColors);
            ps.transform.position = position;
            ps.GetComponent<ParticleSystemRenderer>().sharedMaterial = ParticleMaterial;
            var main = ps.main;
            main.stopAction = ParticleSystemStopAction.Destroy;
            ps.Play();
            ps.Emit(count);
        }

        /// <summary>A particle system that only emits when told to (call <c>Emit</c> on it), e.g. for skid smoke.</summary>
        public static ParticleSystem CreateEmitter(string name, Color color, float speed, float size, float lifetime, float gravity = 0f)
        {
            ParticleSystem ps = Create(name, null, color, speed, size, lifetime, gravity, null);
            var main = ps.main;
            main.loop = true;
            main.maxParticles = 500;
            ps.Play();
            return ps;
        }

        /// <summary>Bright mixed colours, for confetti.</summary>
        public static Gradient Confetti()
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1f, 0.3f, 0.3f), 0f), new GradientColorKey(new Color(1f, 0.85f, 0.2f), 0.33f),
                    new GradientColorKey(new Color(0.3f, 0.9f, 0.4f), 0.66f), new GradientColorKey(new Color(0.3f, 0.6f, 1f), 1f),
                },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return gradient;
        }

        private static ParticleSystem Create(string name, Transform parent, Color color, float speed, float size,
            float lifetime, float gravity, Gradient randomColors)
        {
            var go = new GameObject(name);
            if (parent) go.transform.SetParent(parent, false);

            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = Mathf.Max(lifetime, 0.05f);
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.6f, lifetime);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.3f, speed);
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.6f, size);
            main.startColor = randomColors != null
                ? new ParticleSystem.MinMaxGradient(randomColors) { mode = ParticleSystemGradientMode.RandomColor }
                : new ParticleSystem.MinMaxGradient(color);
            main.gravityModifier = gravity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 300;

            var emission = ps.emission;
            emission.enabled = false;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.2f;

            // Fade out and grow a little over their life
            var colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
            colorOverLifetime.color = fade;

            var sizeOverLifetime = ps.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.7f, 1f, 1.3f));

            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = ParticleMaterial;
            return ps;
        }

        private static Texture2D SoftDot()
        {
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                float alpha = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha * alpha * (3f - 2f * alpha));
            }
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }
    }
}
