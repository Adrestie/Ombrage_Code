// OceanProfile.cs
// Asset réutilisable contenant la liste des modules de features océan (stockés en SOUS-ASSETS,
// comme un VolumeProfile contient ses VolumeComponent). Appliqué par un OceanSystem.
// L'ajout/suppression de modules (sous-assets) est géré côté éditeur (OceanProfileEditor).
// Calqué 1:1 sur TerrainProfile.
using System.Collections.Generic;
using UnityEngine;

namespace Ombrage.OceanFeatures
{
    [CreateAssetMenu(fileName = "OceanProfile", menuName = "Ombrage/Ocean/Ocean Profile", order = 0)]
    public class OceanProfile : ScriptableObject
    {
        [SerializeField] public List<OceanFeatureModule> modules = new List<OceanFeatureModule>();

        public T Get<T>() where T : OceanFeatureModule
        {
            for (int i = 0; i < modules.Count; i++)
                if (modules[i] is T t) return t;
            return null;
        }

        public bool Has<T>() where T : OceanFeatureModule => Get<T>() != null;

        public bool Has(System.Type type)
        {
            for (int i = 0; i < modules.Count; i++)
                if (modules[i] != null && modules[i].GetType() == type) return true;
            return false;
        }
    }
}
