// TerrainRampBaker.cs
// Utilitaire partagé : bake une AnimationCurve en texture rampe RFloat 256x1 + hash de contenu
// pour ne rebake que si la courbe change. Réutilisé par les modules Tessellation et Sand.
// (Extrait de TerrainLitCustomSetup.BakeTessellationRamp/BakeGlitterRamp/ComputeCurveContentHash.)
using UnityEngine;

namespace Ombrage.TerrainFeatures
{
    public static class TerrainRampBaker
    {
        public const int Width = 256;

        public static int ContentHash(AnimationCurve curve)
        {
            int hash = 17;
            if (curve != null)
            {
                var keys = curve.keys;
                for (int i = 0; i < keys.Length; i++)
                {
                    var k = keys[i];
                    hash = hash * 31 + k.time.GetHashCode();
                    hash = hash * 31 + k.value.GetHashCode();
                    hash = hash * 31 + k.inTangent.GetHashCode();
                    hash = hash * 31 + k.outTangent.GetHashCode();
                }
            }
            return hash;
        }

        /// Bake si nécessaire. Retourne true si (re)baké. Le tex (HideAndDontSave) appartient à l'appelant.
        public static bool Bake(AnimationCurve curve, ref Texture2D tex, ref int cachedHash, string name)
        {
            if (curve == null || curve.length == 0) return false;

            int hash = ContentHash(curve);
            if (tex != null && hash == cachedHash) return false;

            if (tex == null)
            {
                tex = new Texture2D(Width, 1, TextureFormat.RFloat, false)
                {
                    name = name,
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                    hideFlags = HideFlags.HideAndDontSave
                };
            }

            var px = new Color[Width];
            for (int i = 0; i < Width; i++)
            {
                float t = (float)i / (Width - 1);
                px[i] = new Color(Mathf.Clamp01(curve.Evaluate(t)), 0f, 0f, 0f);
            }
            tex.SetPixels(px);
            tex.Apply(false, false);
            cachedHash = hash;
            return true;
        }

        public static void Release(ref Texture2D tex)
        {
            if (tex != null)
            {
#if UNITY_EDITOR
                if (!Application.isPlaying) Object.DestroyImmediate(tex); else Object.Destroy(tex);
#else
                Object.Destroy(tex);
#endif
                tex = null;
            }
        }
    }
}
