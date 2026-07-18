using UnityEngine;

namespace Ombrage.Tools.VertexColorEditor
{
    /// <summary>
    /// Masque des canaux RGBA à modifier lors du painting.
    /// </summary>
    public struct ChannelMask
    {
        public bool R, G, B, A;

        public ChannelMask(bool r, bool g, bool b, bool a)
        {
            R = r; G = g; B = b; A = a;
        }

        public bool None => !R && !G && !B && !A;
    }

    /// <summary>
    /// Logique cœur du painting : raycast manuel sur le mesh et application du
    /// brush sphérique. La peinture peut être restreinte par un masque de vertices
    /// (bool[] indexé par vertex ; null = aucun masque).
    /// </summary>
    public class VertexColorPainter
    {
        Vector3[] _vertices;
        int[] _triangles;
        int _vertexCount;

        public bool IsReady => _vertices != null && _triangles != null;
        public int VertexCount => _vertexCount;
        public int TriangleCount => _triangles != null ? _triangles.Length / 3 : 0;

        /// <summary>
        /// Met en cache la géométrie du mesh de travail.
        /// </summary>
        public void SetMesh(Mesh mesh)
        {
            if (mesh == null)
            {
                _vertices = null;
                _triangles = null;
                _vertexCount = 0;
                return;
            }

            _vertices = mesh.vertices;
            _triangles = mesh.triangles;
            _vertexCount = mesh.vertexCount;
        }

        /// <summary>
        /// Indices des 3 vertices d'un triangle (face).
        /// </summary>
        public void GetTriangleVertices(int triangleIndex, out int v0, out int v1, out int v2)
        {
            int t = triangleIndex * 3;
            v0 = _triangles[t];
            v1 = _triangles[t + 1];
            v2 = _triangles[t + 2];
        }

        public struct RayHit
        {
            public bool hit;
            public Vector3 pointWorld;
            public Vector3 normalWorld;
            public int triangleIndex;
        }

        /// <summary>
        /// Raycast manuel (Möller–Trumbore) d'un rayon monde sur le mesh.
        /// Aucune dépendance à la physique : fonctionne hors Play Mode.
        /// </summary>
        public RayHit Raycast(Ray worldRay, Matrix4x4 localToWorld)
        {
            var result = new RayHit { hit = false };
            if (!IsReady)
                return result;

            Matrix4x4 worldToLocal = localToWorld.inverse;
            Vector3 ro = worldToLocal.MultiplyPoint3x4(worldRay.origin);
            Vector3 rd = worldToLocal.MultiplyVector(worldRay.direction).normalized;

            float closest = float.MaxValue;
            int hitTri = -1;
            Vector3 hitLocal = Vector3.zero;

            for (int t = 0; t < _triangles.Length; t += 3)
            {
                Vector3 v0 = _vertices[_triangles[t]];
                Vector3 v1 = _vertices[_triangles[t + 1]];
                Vector3 v2 = _vertices[_triangles[t + 2]];

                if (RayTriangle(ro, rd, v0, v1, v2, out float dist) && dist < closest)
                {
                    closest = dist;
                    hitTri = t;
                    hitLocal = ro + rd * dist;
                }
            }

            if (hitTri >= 0)
            {
                Vector3 a = _vertices[_triangles[hitTri]];
                Vector3 b = _vertices[_triangles[hitTri + 1]];
                Vector3 c = _vertices[_triangles[hitTri + 2]];
                Vector3 nLocal = Vector3.Cross(b - a, c - a).normalized;

                result.hit = true;
                result.pointWorld = localToWorld.MultiplyPoint3x4(hitLocal);
                result.normalWorld = localToWorld.MultiplyVector(nLocal).normalized;
                result.triangleIndex = hitTri / 3;
            }

            return result;
        }

        // Möller–Trumbore. internal : réutilisé par ScreenOcclusionGrid.
        internal static bool RayTriangle(Vector3 ro, Vector3 rd,
            Vector3 v0, Vector3 v1, Vector3 v2, out float distance)
        {
            const float EPS = 1e-7f;
            distance = 0f;

            Vector3 e1 = v1 - v0;
            Vector3 e2 = v2 - v0;
            Vector3 p = Vector3.Cross(rd, e2);
            float det = Vector3.Dot(e1, p);

            if (det > -EPS && det < EPS)
                return false;

            float invDet = 1f / det;
            Vector3 tvec = ro - v0;

            float u = Vector3.Dot(tvec, p) * invDet;
            if (u < 0f || u > 1f)
                return false;

            Vector3 q = Vector3.Cross(tvec, e1);
            float v = Vector3.Dot(rd, q) * invDet;
            if (v < 0f || u + v > 1f)
                return false;

            float dist = Vector3.Dot(e2, q) * invDet;
            if (dist <= EPS)
                return false;

            distance = dist;
            return true;
        }

        /// <summary>
        /// Applique le brush sphérique sur le tableau de couleurs.
        /// Si vertexMask est non null, seuls les vertices marqués true sont affectés.
        /// </summary>
        public bool ApplyBrush(Color[] colors, Matrix4x4 localToWorld, Vector3 centerWorld,
            VertexColorBrush brush, ChannelMask channels, float strokeWeight,
            bool[] vertexMask)
        {
            if (!IsReady || colors == null || colors.Length != _vertexCount)
                return false;
            if (channels.None)
                return false;

            float radius = Mathf.Max(0.0001f, brush.radius);
            float sqrRadius = radius * radius;
            bool masked = vertexMask != null && vertexMask.Length == _vertexCount;
            bool changed = false;

            for (int i = 0; i < _vertexCount; i++)
            {
                if (masked && !vertexMask[i])
                    continue;

                Vector3 worldPos = localToWorld.MultiplyPoint3x4(_vertices[i]);
                float sqrDist = (worldPos - centerWorld).sqrMagnitude;
                if (sqrDist > sqrRadius)
                    continue;

                float normalized = Mathf.Sqrt(sqrDist) / radius;
                float weight = brush.EvaluateFalloff(normalized) * brush.strength * strokeWeight;
                if (weight <= 0f)
                    continue;

                colors[i] = ApplyChannels(colors[i], brush.color, brush.alphaValue,
                    channels, weight);
                changed = true;
            }

            return changed;
        }

        /// <summary>
        /// Remplit la zone (masquée, ou tout le mesh) avec la couleur / l'alpha
        /// cible, mélangée par 'strength'.
        /// </summary>
        public bool ApplyFill(Color[] colors, Color rgb, float alphaValue,
            ChannelMask channels, float strength, bool[] vertexMask)
        {
            if (!IsReady || colors == null || colors.Length != _vertexCount)
                return false;
            if (channels.None || strength <= 0f)
                return false;

            bool masked = vertexMask != null && vertexMask.Length == _vertexCount;
            bool changed = false;

            for (int i = 0; i < _vertexCount; i++)
            {
                if (masked && !vertexMask[i])
                    continue;
                colors[i] = ApplyChannels(colors[i], rgb, alphaValue, channels, strength);
                changed = true;
            }
            return changed;
        }

        /// <summary>
        /// Applique un dégradé en ESPACE ÉCRAN. La ligne est définie par deux points
        /// GUI (startGui -> endGui) tels que tracés dans la SceneView. Chaque vertex
        /// est converti en position écran via la caméra, puis projeté sur le segment
        /// écran (t dans [0,1]). La profondeur n'intervient pas : le dégradé suit la
        /// vue sous laquelle il a été tracé.
        /// </summary>
        public bool ApplyGradient(Color[] colors, Matrix4x4 localToWorld,
            Camera camera, Vector2 startGui, Vector2 endGui, Gradient gradient,
            ChannelMask channels, float strength, bool[] vertexMask)
        {
            if (!IsReady || colors == null || colors.Length != _vertexCount)
                return false;
            if (channels.None || strength <= 0f || gradient == null || camera == null)
                return false;

            Vector2 dir = endGui - startGui;
            float len2 = dir.sqrMagnitude;
            if (len2 < 1e-6f)
                return false;

            bool masked = vertexMask != null && vertexMask.Length == _vertexCount;
            bool changed = false;

            for (int i = 0; i < _vertexCount; i++)
            {
                if (masked && !vertexMask[i])
                    continue;
                Vector3 wp = localToWorld.MultiplyPoint3x4(_vertices[i]);
                Vector2 gui = WorldToGui(camera, wp);
                float t = Mathf.Clamp01(Vector2.Dot(gui - startGui, dir) / len2);
                Color gc = gradient.Evaluate(t);
                colors[i] = ApplyChannels(colors[i], gc, gc.a, channels, strength);
                changed = true;
            }
            return changed;
        }

        // Position GUI (origine en haut à gauche, y vers le bas) d'un point monde,
        // cohérente avec HandleUtility.WorldToGUIPoint utilisé lors du tracé.
        static Vector2 WorldToGui(Camera camera, Vector3 worldPoint)
        {
            Vector3 sp = camera.WorldToScreenPoint(worldPoint);
            return new Vector2(sp.x, camera.pixelHeight - sp.y);
        }

        /// <summary>
        /// Lissage : chaque vertex de la zone du brush voit ses canaux tirés vers
        /// la moyenne de ses voisins (adjacence topologique soudée). Les voisins
        /// sont lus sur un snapshot, donc le résultat ne dépend pas de l'ordre.
        /// </summary>
        public bool ApplySmooth(Color[] colors, Matrix4x4 localToWorld,
            Vector3 centerWorld, VertexColorBrush brush, ChannelMask channels,
            float strokeWeight, bool[] vertexMask, VertexMeshTopology topology)
        {
            if (!IsReady || colors == null || colors.Length != _vertexCount)
                return false;
            if (channels.None || topology == null || !topology.IsReady)
                return false;

            float radius = Mathf.Max(0.0001f, brush.radius);
            float sqrRadius = radius * radius;
            bool masked = vertexMask != null && vertexMask.Length == _vertexCount;

            Color[] snapshot = (Color[])colors.Clone();
            bool changed = false;

            for (int i = 0; i < _vertexCount; i++)
            {
                if (masked && !vertexMask[i])
                    continue;

                Vector3 worldPos = localToWorld.MultiplyPoint3x4(_vertices[i]);
                float sqrDist = (worldPos - centerWorld).sqrMagnitude;
                if (sqrDist > sqrRadius)
                    continue;

                float normalized = Mathf.Sqrt(sqrDist) / radius;
                float weight = brush.EvaluateFalloff(normalized) * brush.strength
                               * strokeWeight;
                if (weight <= 0f)
                    continue;

                Color avg = topology.GetNeighborAverage(i, snapshot);
                colors[i] = ApplyChannels(colors[i], avg, avg.a, channels, weight);
                changed = true;
            }
            return changed;
        }

        /// <summary>
        /// Mélange les canaux activés de 'current' vers la cible, avec le poids donné.
        /// </summary>
        static Color ApplyChannels(Color current, Color rgb, float alphaValue,
            ChannelMask channels, float weight)
        {
            if (channels.R) current.r = Mathf.Lerp(current.r, rgb.r, weight);
            if (channels.G) current.g = Mathf.Lerp(current.g, rgb.g, weight);
            if (channels.B) current.b = Mathf.Lerp(current.b, rgb.b, weight);
            if (channels.A) current.a = Mathf.Lerp(current.a, alphaValue, weight);
            return current;
        }
    }
}
