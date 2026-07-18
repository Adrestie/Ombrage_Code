using System.Collections.Generic;
using UnityEngine;

namespace Ombrage.Tools.UVEditor
{
    /// <summary>
    /// Helpers de lecture des canaux UV d'un mesh. L'outil cible un canal parmi
    /// UV0 / UV1 / UV2, choisi par l'utilisateur.
    /// </summary>
    public static class UVChannelUtils
    {
        /// <summary>Nombre de canaux UV gérés par l'outil (UV0..UV2).</summary>
        public const int ChannelCount = 3;

        /// <summary>
        /// Retourne les UV du canal demandé. Si le canal est vide ou incohérent
        /// avec le mesh, retourne un tableau de zéros de la taille du mesh.
        /// </summary>
        public static Vector2[] GetChannel(Mesh mesh, int channel)
        {
            if (mesh == null)
                return System.Array.Empty<Vector2>();

            var list = new List<Vector2>();
            mesh.GetUVs(channel, list);
            if (list.Count == mesh.vertexCount)
                return list.ToArray();

            return new Vector2[mesh.vertexCount];
        }

        /// <summary>Vrai si le canal contient des UV cohérentes avec le mesh.</summary>
        public static bool HasChannel(Mesh mesh, int channel)
        {
            if (mesh == null)
                return false;

            var list = new List<Vector2>();
            mesh.GetUVs(channel, list);
            return list.Count == mesh.vertexCount;
        }

        /// <summary>Libellé court d'un canal (UV0 / UV1 / UV2).</summary>
        public static string ChannelLabel(int channel)
        {
            switch (channel)
            {
                case 0: return "UV0";
                case 1: return "UV1";
                case 2: return "UV2";
                default: return "UV" + channel;
            }
        }
    }
}
