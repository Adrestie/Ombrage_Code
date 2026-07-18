// TerrainProfile.cs
// Asset réutilisable contenant la liste des modules de features terrain (stockés en SOUS-ASSETS,
// comme un VolumeProfile contient ses VolumeComponent). Appliqué par un TerrainProfileController.
// L'ajout/suppression de modules (sous-assets) est géré côté éditeur.
using System.Collections.Generic;
using UnityEngine;

namespace Ombrage.TerrainFeatures
{
    [CreateAssetMenu(fileName = "TerrainProfile", menuName = "Ombrage/Terrain/Terrain Profile", order = 0)]
    public class TerrainProfile : ScriptableObject
    {
        [SerializeField] public List<TerrainFeatureModule> modules = new List<TerrainFeatureModule>();

        public T Get<T>() where T : TerrainFeatureModule
        {
            for (int i = 0; i < modules.Count; i++)
                if (modules[i] is T t) return t;
            return null;
        }

        public bool Has<T>() where T : TerrainFeatureModule => Get<T>() != null;

        public bool Has(System.Type type)
        {
            for (int i = 0; i < modules.Count; i++)
                if (modules[i] != null && modules[i].GetType() == type) return true;
            return false;
        }
    }
}
