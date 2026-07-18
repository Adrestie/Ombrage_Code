// OceanModuleMenuAttribute.cs
// Donne le libellé/chemin d'un module dans le menu « Add Module » de l'éditeur de profil océan
// (équivalent de [VolumeComponentMenu] côté HDRP, calqué sur TerrainModuleMenuAttribute).
using System;

namespace Ombrage.OceanFeatures
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class OceanModuleMenuAttribute : Attribute
    {
        public readonly string menuPath;
        public readonly string displayName;

        public OceanModuleMenuAttribute(string menuPath)
        {
            this.menuPath = menuPath;
            int idx = menuPath != null ? menuPath.LastIndexOf('/') : -1;
            displayName = (idx >= 0) ? menuPath.Substring(idx + 1) : (menuPath ?? "Module");
        }
    }
}
