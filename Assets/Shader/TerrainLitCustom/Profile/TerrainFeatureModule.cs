// TerrainFeatureModule.cs
// Base d'un module de feature terrain (équivalent VolumeComponent HDRP), stocké en SOUS-ASSET
// d'un TerrainProfile. Chaque feature shader = une sous-classe.
//
// Cycle : OnModuleEnable (ressources) -> Apply (props statiques, chaque frame) + Tick (dynamique)
//         -> OnModuleDisable (libération). GetMaxVertexDisplacement alimente les patch bounds.
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Ombrage.TerrainFeatures
{
    public abstract class TerrainFeatureModule : ScriptableObject
    {
        [Tooltip("Active le module (et son keyword shader).")]
        [SerializeField] public bool active = true;

        public virtual string DisplayName
        {
            get
            {
                var attr = (TerrainModuleMenuAttribute)System.Attribute.GetCustomAttribute(GetType(), typeof(TerrainModuleMenuAttribute));
                return attr != null ? attr.displayName : GetType().Name;
            }
        }

        /// Keyword shader gardé par ce module (null = aucun).
        public virtual string Keyword => null;
        /// Le keyword doit-il être ON ? (par défaut = actif ; surchargé pour le gating, ex. vent).
        public virtual bool KeywordEnabled(TerrainApplyContext ctx) => active;

        /// Le module active-t-il la déformation de vertex (tessellation/displacement) ?
        public virtual bool EnablesVertexDisplacement => false;

        public virtual void OnModuleEnable(TerrainApplyContext ctx) { }
        public virtual void OnModuleDisable(TerrainApplyContext ctx) { }

        /// Pousse les propriétés statiques au matériau (respecte overrideState).
        public abstract void Apply(TerrainApplyContext ctx);

        /// Travail dynamique par frame (temps de vent, direction soleil, déformation...).
        public virtual void Tick(TerrainApplyContext ctx) { }

        /// Déplacement vertical max (m) contribué aux patch bounds du terrain.
        public virtual float GetMaxVertexDisplacement() => 0f;

        public virtual void DrawGizmos(TerrainApplyContext ctx) { }

        // --- Réflexion : collecte des paramètres (éditeur + All/None) ---
        public List<TerrainParameter> CollectParameters()
        {
            var list = new List<TerrainParameter>();
            var fields = GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (var f in fields)
            {
                if (typeof(TerrainParameter).IsAssignableFrom(f.FieldType))
                {
                    if (f.GetValue(this) is TerrainParameter p) list.Add(p);
                }
            }
            return list;
        }

        public void SetAllOverrides(bool state)
        {
            foreach (var p in CollectParameters()) p.overrideState = state;
        }

        // --- Helpers de push (override = contrôle : on n'écrit que si overrideState) ---
        // ⚠️ Ces surcharges SANS défaut ne réécrivent pas quand l'override est OFF : le matériau garde la
        // dernière valeur poussée (pas de retour à un défaut). Préférer les surcharges AVEC `dflt` ci-dessous,
        // qui écrivent TOUJOURS (valeur si override, sinon défaut) → décocher un override redevient effectif.
        protected static void Push(Material m, int id, FloatParameter p) { if (p.overrideState) m.SetFloat(id, p.value); }
        protected static void Push(Material m, int id, IntParameter p) { if (p.overrideState) m.SetFloat(id, p.value); }
        protected static void Push(Material m, int id, BoolParameter p) { if (p.overrideState) m.SetFloat(id, p.value ? 1f : 0f); }
        protected static void Push(Material m, int id, ColorParameter p, bool linear = false) { if (p.overrideState) m.SetColor(id, linear ? p.value.linear : p.value); }
        protected static void Push(Material m, int id, Texture2DParameter p) { if (p.overrideState && p.value != null) m.SetTexture(id, p.value); }
        protected static void PushVector(Material m, int id, Vector2Parameter p) { if (p.overrideState) m.SetVector(id, new Vector4(p.value.x, p.value.y, 0f, 0f)); }

        // --- Helpers de push AVEC défaut : écrivent toujours (override ON -> valeur, OFF -> défaut). ---
        protected static void Push(Material m, int id, FloatParameter p, float dflt) { m.SetFloat(id, p.overrideState ? p.value : dflt); }
        protected static void Push(Material m, int id, IntParameter p, int dflt) { m.SetFloat(id, p.overrideState ? p.value : dflt); }
        protected static void Push(Material m, int id, ColorParameter p, Color dflt, bool linear = false) { Color c = p.overrideState ? p.value : dflt; m.SetColor(id, linear ? c.linear : c); }
    }
}
