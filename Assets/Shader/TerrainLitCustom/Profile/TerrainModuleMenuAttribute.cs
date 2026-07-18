// TerrainModuleMenuAttribute.cs
// Donne le libellé/chemin d'un module dans le menu « Add Override » de l'éditeur de profil
// (équivalent de [VolumeComponentMenu] côté HDRP).
using System;

namespace Ombrage.TerrainFeatures
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class TerrainModuleMenuAttribute : Attribute
    {
        public readonly string menuPath;
        public readonly string displayName;

        public TerrainModuleMenuAttribute(string menuPath)
        {
            this.menuPath = menuPath;
            int idx = menuPath != null ? menuPath.LastIndexOf('/') : -1;
            displayName = (idx >= 0) ? menuPath.Substring(idx + 1) : (menuPath ?? "Module");
        }
    }
}
