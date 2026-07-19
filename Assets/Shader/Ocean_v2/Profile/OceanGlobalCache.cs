// OceanGlobalCache.cs
// Harnais de PUSH GLOBAL non cumulatif et restaurable (contrat anti-bug n°1).
//
// Pourquoi : l'ancien système modifiait le soleil de façon CUMULATIVE (sun.intensity *= k)
// sans jamais restaurer en [ExecuteAlways], assombrissant le soleil de manière irréversible.
// Ici, TOUTE écriture de global océan passe par ce cache :
//   - assignation PURE (jamais *= / +=) ;
//   - mémorisation de chaque global poussé (pour pouvoir restaurer une valeur neutre au Teardown) ;
//   - caching (on ne réémet pas un Set* si la valeur n'a pas changé).
//
// Actuellement aucun global métier n'est poussé. Seul le CONTRAT/harnais est posé, prêt pour la suite.
using System.Collections.Generic;
using UnityEngine;

namespace Ombrage.OceanFeatures
{
    /// Cache de propriétés globales shader (Shader.SetGlobal*) : assignation pure, anti-cumul,
    /// restaurable en valeur neutre. Une instance est détenue par OceanSystem et exposée aux
    /// modules via OceanApplyContext.
    public sealed class OceanGlobalCache
    {
        // Valeurs poussées, par id de propriété. Sert au caching (skip si inchangé) ET au reset.
        readonly Dictionary<int, float> m_Floats = new Dictionary<int, float>();
        readonly Dictionary<int, Vector4> m_Vectors = new Dictionary<int, Vector4>();
        readonly Dictionary<int, Color> m_Colors = new Dictionary<int, Color>();
        readonly Dictionary<int, Texture> m_Textures = new Dictionary<int, Texture>();

        /// Pousse un float global (assignation pure). Ne réémet pas si la valeur est inchangée.
        public void SetGlobalFloat(int id, float value)
        {
            if (m_Floats.TryGetValue(id, out var cur) && cur == value) return;
            m_Floats[id] = value;
            Shader.SetGlobalFloat(id, value); // assignation pure, JAMAIS *= / +=
        }

        /// Pousse un vecteur global (assignation pure). Ne réémet pas si inchangé.
        public void SetGlobalVector(int id, Vector4 value)
        {
            if (m_Vectors.TryGetValue(id, out var cur) && cur == value) return;
            m_Vectors[id] = value;
            Shader.SetGlobalVector(id, value);
        }

        /// Pousse une couleur globale (assignation pure). Ne réémet pas si inchangée.
        public void SetGlobalColor(int id, Color value)
        {
            if (m_Colors.TryGetValue(id, out var cur) && cur == value) return;
            m_Colors[id] = value;
            Shader.SetGlobalColor(id, value);
        }

        /// Pousse une texture globale. Ne réémet pas si inchangée.
        public void SetGlobalTexture(int id, Texture value)
        {
            if (m_Textures.TryGetValue(id, out var cur) && cur == value) return;
            m_Textures[id] = value;
            Shader.SetGlobalTexture(id, value);
        }

        /// Restaure tous les globaux poussés à une valeur NEUTRE (0 / zéro / clair / null) et vide
        /// le cache. À appeler au Teardown du OceanSystem (OnDisable) : garantit qu'aucune écriture
        /// océan ne « fuit » après désactivation (contrat anti-bug n°1).
        public void RestoreAll()
        {
            foreach (var kv in m_Floats) Shader.SetGlobalFloat(kv.Key, 0f);
            foreach (var kv in m_Vectors) Shader.SetGlobalVector(kv.Key, Vector4.zero);
            foreach (var kv in m_Colors) Shader.SetGlobalColor(kv.Key, Color.clear);
            foreach (var kv in m_Textures) Shader.SetGlobalTexture(kv.Key, null);
            Clear();
        }

        /// Vide le cache sans réémettre (ré-init de l'état de tracking au Setup).
        public void Clear()
        {
            m_Floats.Clear();
            m_Vectors.Clear();
            m_Colors.Clear();
            m_Textures.Clear();
        }
    }
}
