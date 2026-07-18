// TerrainApplyContext.cs
// Contexte passé aux modules à chaque Apply/Tick. Porte le matériau cible, les terrains,
// le nombre de couches, le profil, le contrôleur (état runtime), et des requêtes inter-modules
// (ex. la déformation de vertex est-elle active, pour gater le vent).
using UnityEngine;

namespace Ombrage.TerrainFeatures
{
    public class TerrainApplyContext
    {
        public Material material;
        public Terrain[] terrains;
        public int layerCount = 4;
        public TerrainProfile profile;
        public TerrainProfileController controller;
        public bool editMode;
        public float time;
        public float deltaTime;

        /// Un module de ce type est-il présent ET actif dans le profil ?
        public bool IsActive<T>() where T : TerrainFeatureModule
        {
            if (profile == null) return false;
            var m = profile.Get<T>();
            return m != null && m.active;
        }

        /// Un module actif active-t-il la déformation de vertex (tessellation/displacement) ?
        /// Sert au gating du vent (qui n'a de sens qu'avec displacement) et aux patch bounds.
        public bool VertexDisplacementActive()
        {
            if (profile == null) return false;
            for (int i = 0; i < profile.modules.Count; i++)
            {
                var m = profile.modules[i];
                if (m != null && m.active && m.EnablesVertexDisplacement) return true;
            }
            return false;
        }

        // --- État runtime par module (RT de déformation, etc.) détenu par le contrôleur ---
        public object GetRuntime(TerrainFeatureModule m) => controller != null ? controller.GetRuntime(m) : null;
        public void SetRuntime(TerrainFeatureModule m, object state) { if (controller != null) controller.SetRuntime(m, state); }
    }
}
