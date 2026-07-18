// TerrainProfileController.cs
// Composant UNIQUE de scène qui applique un TerrainProfile au matériau terrain PARTAGÉ.
// - Pousse props + keywords (scene-wide) à chaque frame (parité avec l'ancien monolithe).
// - Tick les features dynamiques (vent, soleil, déformation).
// - Applique patchBoundsMultiplier à TOUS les terrains partageant le matériau.
// - Porte les refs de scène + l'état runtime des modules (un SO ne sérialise pas de refs de scène).
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Ombrage.TerrainFeatures
{
    [ExecuteAlways]
    [AddComponentMenu("Ombrage/Terrain/Terrain Profile Controller")]
    public class TerrainProfileController : MonoBehaviour
    {
        [Tooltip("Profil de features terrain à appliquer.")]
        public TerrainProfile profile;

        [Tooltip("Matériau cible. Vide = déduit du Terrain de ce GameObject, sinon du premier terrain actif.")]
        public Material materialOverride;

        [Header("Références de scène (déformation)")]
        [Tooltip("Carrosserie du véhicule (déformation). Requis pour le module Deformation.")]
        public Transform vehicleBody;
        public Transform[] wheels = new Transform[6];
        public LayerMask groundLayer = ~0;

        Material m_Material;
        Terrain[] m_Terrains;
        TerrainApplyContext m_Ctx;
        readonly Dictionary<TerrainFeatureModule, object> m_Runtime = new Dictionary<TerrainFeatureModule, object>();
        readonly List<TerrainFeatureModule> m_Enabled = new List<TerrainFeatureModule>();

        public Material ResolvedMaterial => m_Material;
        public Terrain[] ResolvedTerrains => m_Terrains;

        // --- Accès déformation (consommé par le shim TerrainDeformationManager → GrassController) ---
        public RenderTexture DeformationRT
        {
            get
            {
                var m = profile != null ? profile.Get<DeformationModule>() : null;
                return m != null ? (GetRuntime(m) as DeformationRuntime)?.DeformationRT : null;
            }
        }

        public float DeformationBufferWorldSize
        {
            get
            {
                var m = profile != null ? profile.Get<DeformationModule>() : null;
                return m != null ? m.BufferWorldSize : 40f;
            }
        }

        // --- État runtime par module ---
        public object GetRuntime(TerrainFeatureModule m) => (m != null && m_Runtime.TryGetValue(m, out var s)) ? s : null;
        public void SetRuntime(TerrainFeatureModule m, object state) { if (m != null) m_Runtime[m] = state; }

        void OnEnable() { Setup(); }
        void OnDisable() { Teardown(); }

        void OnValidate()
        {
            if (wheels == null) wheels = new Transform[6];
#if UNITY_EDITOR
            // Re-setup différé (le profil/les refs ont pu changer).
            EditorApplication.delayCall += DeferredResetup;
#endif
        }

#if UNITY_EDITOR
        void DeferredResetup()
        {
            if (this == null) return;
            if (!isActiveAndEnabled) return;
            Teardown();
            Setup();
        }
#endif

        void Setup()
        {
            ResolveTargets();
            BuildContext();
            m_Enabled.Clear();
            if (profile != null)
            {
                foreach (var m in profile.modules)
                {
                    if (m == null) continue;
                    m.OnModuleEnable(m_Ctx);
                    m_Enabled.Add(m);
                }
            }
            ApplyAll();
        }

        void Teardown()
        {
            if (m_Ctx != null)
            {
                foreach (var m in m_Enabled)
                    if (m != null) m.OnModuleDisable(m_Ctx);
            }
            m_Enabled.Clear();
            m_Runtime.Clear();
        }

        void ResolveTargets()
        {
            m_Material = materialOverride;
            var all = Terrain.activeTerrains;

            if (m_Material == null)
            {
                var self = GetComponent<Terrain>();
                if (self != null && self.materialTemplate != null)
                    m_Material = self.materialTemplate;
                else if (all != null)
                {
                    foreach (var t in all)
                        if (t != null && t.materialTemplate != null) { m_Material = t.materialTemplate; break; }
                }
            }

            var list = new List<Terrain>();
            if (all != null && m_Material != null)
            {
                foreach (var t in all)
                    if (t != null && t.materialTemplate == m_Material) list.Add(t);
            }
            m_Terrains = list.ToArray();
        }

        void BuildContext()
        {
            m_Ctx = new TerrainApplyContext
            {
                material = m_Material,
                terrains = m_Terrains,
                profile = profile,
                controller = this,
                editMode = !Application.isPlaying
            };
            RefreshLayerCount();
        }

        void RefreshLayerCount()
        {
            if (m_Ctx == null) return;
            m_Ctx.layerCount = (m_Material != null && m_Material.IsKeywordEnabled("_TERRAIN_8_LAYERS")) ? 8 : 4;
        }

        void Update()
        {
            if (m_Material == null || m_Ctx == null)
            {
                ResolveTargets();
                BuildContext();
                if (m_Material == null) return;
            }

            m_Ctx.material = m_Material;
            m_Ctx.terrains = m_Terrains;
            m_Ctx.profile = profile;
            m_Ctx.editMode = !Application.isPlaying;
            m_Ctx.time = Application.isPlaying ? Time.time : EditorTime();
            m_Ctx.deltaTime = Time.deltaTime;
            RefreshLayerCount();

            ApplyAll();
            Tick();
            UpdatePatchBounds();

#if UNITY_EDITOR
            if (!Application.isPlaying) SceneView.RepaintAll();
#endif
        }

        static float EditorTime()
        {
#if UNITY_EDITOR
            return (float)EditorApplication.timeSinceStartup;
#else
            return Time.time;
#endif
        }

        void ApplyAll()
        {
            if (m_Material == null || profile == null || m_Ctx == null) return;
            foreach (var m in profile.modules)
            {
                if (m == null) continue;
                if (!string.IsNullOrEmpty(m.Keyword))
                    SetKeyword(m_Material, m.Keyword, m.KeywordEnabled(m_Ctx));
                if (m.active) m.Apply(m_Ctx);
            }
        }

        void Tick()
        {
            if (profile == null || m_Ctx == null) return;
            foreach (var m in profile.modules)
                if (m != null && m.active) m.Tick(m_Ctx);
        }

        void UpdatePatchBounds()
        {
            if (m_Terrains == null || profile == null) return;

            // Somme des contributions de déplacement (Tessellation + Sand rim + Wind), gatée sur
            // la présence d'un module activant le déplacement de vertex (parité monolithe).
            float maxDisp = 0f;
            bool dispActive = false;
            foreach (var m in profile.modules)
            {
                if (m == null || !m.active) continue;
                maxDisp += m.GetMaxVertexDisplacement();
                if (m.EnablesVertexDisplacement) dispActive = true;
            }

            foreach (var t in m_Terrains)
            {
                if (t == null || t.terrainData == null) continue;
                if (dispActive && maxDisp > 0f)
                {
                    float terrainHeight = Mathf.Max(t.terrainData.size.y, 0.01f);
                    float boundsY = Mathf.Clamp(maxDisp * 65535f / terrainHeight, 2f, 10000f);
                    t.patchBoundsMultiplier = new Vector3(1f, boundsY, 1f);
                }
                else
                {
                    t.patchBoundsMultiplier = Vector3.one;
                }
            }
        }

        void OnDrawGizmosSelected()
        {
            if (profile == null || m_Ctx == null) return;
            foreach (var m in profile.modules)
                if (m != null && m.active) m.DrawGizmos(m_Ctx);
        }

        static void SetKeyword(Material m, string kw, bool on)
        {
            if (on) m.EnableKeyword(kw);
            else m.DisableKeyword(kw);
        }
    }
}
