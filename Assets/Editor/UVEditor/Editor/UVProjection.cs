using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ombrage.Tools.UVEditor
{
    /// <summary>
    /// Applique une projection UV sur le mesh de travail.
    ///
    /// Pipeline :
    ///  1. Un job Burst (<see cref="UVProjectionJob"/>) calcule l'UV cible de
    ///     chaque COIN de triangle. Les coins de faces non masquées conservent
    ///     l'UV existante du canal édité.
    ///  2. Le mesh est reconstruit : un vertex d'origine n'est dédoublé que si
    ///     ses coins exigent des UV différentes sur le canal édité. Cela crée
    ///     exactement les coutures nécessaires — celles de la box (faces sur des
    ///     plans différents) et celles entre faces projetées et faces conservées
    ///     — en reportant positions, normales, couleurs et les autres canaux UV.
    ///
    /// L'ordre et le nombre des triangles sont préservés : une sélection de
    /// faces (par index de triangle) reste valide après une projection.
    ///
    /// Phase 1 : meshes à un seul sous-mesh.
    /// </summary>
    public static class UVProjection
    {
        // Quantum de fusion des UV : deux coins d'un même vertex sont fusionnés
        // si leurs UV coïncident à 1e-5 près sur le canal édité.
        const float WeldQuantum = 100000f;

        public struct Result
        {
            public bool success;
            public string message;
            public int vertexCountBefore;
            public int vertexCountAfter;
        }

        /// <summary>
        /// Projette les UV du <paramref name="channel"/> sur le mesh.
        /// <paramref name="triangleMask"/> à null = projection sur tout le mesh.
        /// </summary>
        public static Result Apply(Mesh mesh, int channel, bool[] triangleMask,
            UVProjectionSettings settings)
        {
            var result = new Result();

            if (mesh == null)
            {
                result.message = "Mesh nul.";
                return result;
            }
            if (settings == null)
            {
                result.message = "Réglages de projection manquants.";
                return result;
            }
            if (mesh.subMeshCount > 1)
            {
                result.message =
                    "Phase 1 : les meshes multi-sous-mesh ne sont pas pris en charge.";
                return result;
            }

            Vector3[] positions = mesh.vertices;
            Vector3[] normals = mesh.normals;
            Color[] colors = mesh.colors;
            int[] triangles = mesh.triangles;

            if (positions == null || positions.Length == 0 ||
                triangles == null || triangles.Length < 3)
            {
                result.message = "Le mesh n'a pas de géométrie exploitable.";
                return result;
            }

            int vCount = positions.Length;
            result.vertexCountBefore = vCount;

            var uv0 = new List<Vector2>(); mesh.GetUVs(0, uv0);
            var uv1 = new List<Vector2>(); mesh.GetUVs(1, uv1);
            var uv2 = new List<Vector2>(); mesh.GetUVs(2, uv2);

            bool hasNormals = normals != null && normals.Length == vCount;
            bool hasColors = colors != null && colors.Length == vCount;
            var srcUV = new List<Vector2>[3];
            srcUV[0] = uv0.Count == vCount ? uv0 : null;
            srcUV[1] = uv1.Count == vCount ? uv1 : null;
            srcUV[2] = uv2.Count == vCount ? uv2 : null;

            // UV existantes du canal édité (servent aux coins non masqués).
            Vector2[] existing = srcUV[channel] != null
                ? srcUV[channel].ToArray()
                : new Vector2[vCount];

            // --- 1. Job Burst : UV cible par coin ---
            Vector2[] cornerUV;
            try
            {
                cornerUV = RunProjectionJob(positions, triangles, existing,
                    triangleMask, settings, mesh.bounds);
            }
            catch (Exception e)
            {
                result.message = "Échec du calcul de projection : " + e.Message;
                return result;
            }

            // --- 2. Reconstruction avec dédoublement minimal ---
            // Le canal édité possède toujours une liste de sortie ; les autres
            // canaux n'en ont une que s'ils existaient sur le mesh source.
            var outUV = new List<Vector2>[3];
            for (int ch = 0; ch < 3; ch++)
            {
                if (ch == channel || srcUV[ch] != null)
                    outUV[ch] = new List<Vector2>(vCount);
            }

            var outPositions = new List<Vector3>(vCount);
            var outNormals = hasNormals ? new List<Vector3>(vCount) : null;
            var outColors = hasColors ? new List<Color>(vCount) : null;
            var outTriangles = new int[triangles.Length];

            // Reconstruction à ORDRE STABLE (option C) :
            //  - les vertices d'origine 0..vCount-1 gardent EXACTEMENT leur
            //    index dans le mesh de sortie ;
            //  - les doublons créés par une couture (un vertex dont les coins
            //    exigent des UV différentes sur le canal édité) sont ajoutés
            //    À LA SUITE, aux indices >= vCount.
            // Ainsi tout outil externe qui adresse les vertices par index
            // (Vertex Color Editor, etc.) reste valide : les N premiers indices
            // ne bougent jamais, et chaque doublon hérite de toutes les données
            // de son vertex parent (position, normale, couleur, autres UV).

            // Pré-remplissage : un slot par vertex d'origine, dans l'ordre.
            for (int v = 0; v < vCount; v++)
            {
                outPositions.Add(positions[v]);
                if (outNormals != null) outNormals.Add(normals[v]);
                if (outColors != null) outColors.Add(colors[v]);
                for (int ch = 0; ch < 3; ch++)
                {
                    if (outUV[ch] == null)
                        continue;
                    // Le canal édité est rempli plus bas (premier coin vu) ;
                    // les autres canaux prennent la valeur d'origine.
                    outUV[ch].Add(ch == channel ? Vector2.zero : srcUV[ch][v]);
                }
            }

            // Pour chaque vertex d'origine : clé UV quantifiée -> index de sortie.
            // La première clé rencontrée occupe le slot d'origine ; les
            // suivantes créent un doublon en fin de tableau.
            var perVertex = new Dictionary<long, int>[vCount];
            var slotAssigned = new bool[vCount]; // slot d'origine déjà attribué ?

            for (int c = 0; c < triangles.Length; c++)
            {
                int ov = triangles[c];
                Vector2 uv = cornerUV[c];
                long key = QuantizeKey(uv);

                Dictionary<long, int> table = perVertex[ov];
                if (table == null)
                {
                    table = new Dictionary<long, int>();
                    perVertex[ov] = table;
                }

                if (!table.TryGetValue(key, out int newIndex))
                {
                    if (!slotAssigned[ov])
                    {
                        // Premier UV de ce vertex : il occupe son slot d'origine.
                        newIndex = ov;
                        slotAssigned[ov] = true;
                        if (outUV[channel] != null)
                            outUV[channel][ov] = uv;
                    }
                    else
                    {
                        // UV différent du même vertex : doublon de couture,
                        // ajouté en fin de tableau, héritant des données parent.
                        newIndex = outPositions.Count;
                        outPositions.Add(positions[ov]);
                        if (outNormals != null) outNormals.Add(normals[ov]);
                        if (outColors != null) outColors.Add(colors[ov]);
                        for (int ch = 0; ch < 3; ch++)
                        {
                            if (outUV[ch] == null)
                                continue;
                            if (ch == channel)
                                outUV[ch].Add(uv);
                            else
                                outUV[ch].Add(srcUV[ch][ov]);
                        }
                    }
                    table[key] = newIndex;
                }

                outTriangles[c] = newIndex;
            }

            // Un vertex d'origine peut n'être référencé par aucun triangle (rare,
            // mais possible) : son slot n'a alors pas reçu d'UV sur le canal
            // édité. On lui laisse son UV d'origine pour rester cohérent.
            if (outUV[channel] != null)
            {
                for (int v = 0; v < vCount; v++)
                {
                    if (!slotAssigned[v])
                    {
                        outUV[channel][v] = srcUV[channel] != null
                            ? srcUV[channel][v]
                            : Vector2.zero;
                    }
                }
            }

            // --- 3. Écriture du mesh reconstruit ---
            try
            {
                mesh.Clear();
                mesh.indexFormat = outPositions.Count > 65535
                    ? IndexFormat.UInt32
                    : IndexFormat.UInt16;

                mesh.SetVertices(outPositions);
                if (outNormals != null)
                    mesh.SetNormals(outNormals);
                if (outColors != null)
                    mesh.SetColors(outColors);
                for (int ch = 0; ch < 3; ch++)
                {
                    if (outUV[ch] != null)
                        mesh.SetUVs(ch, outUV[ch]);
                }

                mesh.subMeshCount = 1;
                mesh.SetTriangles(outTriangles, 0);

                if (outNormals == null)
                    mesh.RecalculateNormals();
                // Les tangentes dépendent d'UV0 : on les recalcule pour HDRP.
                mesh.RecalculateTangents();
                mesh.RecalculateBounds();
            }
            catch (Exception e)
            {
                result.message = "Échec de l'écriture du mesh : " + e.Message;
                return result;
            }

            result.success = true;
            result.vertexCountAfter = outPositions.Count;
            int added = result.vertexCountAfter - result.vertexCountBefore;
            result.message = added > 0
                ? $"Projection appliquée — {added} vertices dédoublés aux coutures " +
                  $"({result.vertexCountBefore} -> {result.vertexCountAfter})."
                : $"Projection appliquée — aucune couture ajoutée " +
                  $"({result.vertexCountAfter} vertices).";
            return result;
        }

        // Exécute le job Burst et récupère les UV par coin.
        static Vector2[] RunProjectionJob(Vector3[] positions, int[] triangles,
            Vector2[] existing, bool[] triangleMask, UVProjectionSettings settings,
            Bounds meshBounds)
        {
            int vCount = positions.Length;
            int cornerCount = triangles.Length;

            var naVerts = new NativeArray<float3>(vCount, Allocator.TempJob);
            var naExisting = new NativeArray<float2>(vCount, Allocator.TempJob);
            for (int i = 0; i < vCount; i++)
            {
                naVerts[i] = positions[i];
                naExisting[i] = existing[i];
            }

            var naTris = new NativeArray<int>(triangles, Allocator.TempJob);

            int maskLen = triangleMask != null ? triangleMask.Length : 0;
            var naMask = new NativeArray<bool>(maskLen, Allocator.TempJob);
            for (int i = 0; i < maskLen; i++)
                naMask[i] = triangleMask[i];

            var naCorner = new NativeArray<float2>(cornerCount, Allocator.TempJob);

            float rot = settings.rotation * Mathf.Deg2Rad;

            var job = new UVProjectionJob
            {
                vertices = naVerts,
                triangles = naTris,
                existingUV = naExisting,
                triangleMask = naMask,
                projectionType = (int)settings.type,
                meshToFrame = settings.MeshToFrameMatrix(meshBounds),
                tiling = settings.tiling,
                offset = settings.offset,
                rotationSin = Mathf.Sin(rot),
                rotationCos = Mathf.Cos(rot),
                cornerUV = naCorner
            };

            try
            {
                job.Schedule(cornerCount, 256).Complete();

                var output = new Vector2[cornerCount];
                for (int i = 0; i < cornerCount; i++)
                    output[i] = naCorner[i];
                return output;
            }
            finally
            {
                naVerts.Dispose();
                naExisting.Dispose();
                naTris.Dispose();
                naMask.Dispose();
                naCorner.Dispose();
            }
        }

        // Clé de fusion : deux composantes UV quantifiées empaquetées dans un long.
        static long QuantizeKey(Vector2 uv)
        {
            int qx = (int)math.round(uv.x * WeldQuantum);
            int qy = (int)math.round(uv.y * WeldQuantum);
            return ((long)(uint)qx << 32) | (uint)qy;
        }
    }
}
