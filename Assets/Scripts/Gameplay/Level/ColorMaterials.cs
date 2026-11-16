using System.Collections.Generic;
using UnityEngine;

namespace Gameplay.Level
{
    /// <summary>One shared plain-colour material per colour, for objects built in code (no assets needed).</summary>
    public static class ColorMaterials
    {
        private static readonly Dictionary<(Color, bool), Material> Cache = new();

        /// <summary>Lit material (URP Lit), for solid objects.</summary>
        public static Material Lit(Color color) => Get(color, false);

        /// <summary>Unlit material (URP Unlit), for markers that should stand out regardless of lighting.</summary>
        public static Material Unlit(Color color) => Get(color, true);

        private static Material Get(Color color, bool unlit)
        {
            if (Cache.TryGetValue((color, unlit), out Material material) && material)
                return material;

            Shader shader = Shader.Find(unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit")
                            ?? Shader.Find(unlit ? "Unlit/Color" : "Standard");
            material = new Material(shader) { color = color };
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            Cache[(color, unlit)] = material;
            return material;
        }
    }
}
