using Unity.Mathematics;
using UnityEngine;

namespace Ombrage.Tools.UVEditor
{
    /// <summary>Type de projection UV.</summary>
    public enum UVProjectionType
    {
        /// <summary>Projection planaire le long de l'axe Z du cadre.</summary>
        Planar = 0,
        /// <summary>Projection sur les 6 faces du cadre, choisies par normale de face.</summary>
        Box = 1,
        /// <summary>Projection cylindrique autour de l'axe Y du cadre.</summary>
        Cylindrical = 2,
        /// <summary>Projection sphérique centrée sur le cadre.</summary>
        Spherical = 3,
        /// <summary>Projection box automatique alignée sur la bounding box locale du mesh.</summary>
        Triplanar = 4
    }

    /// <summary>Poignée de manipulation du cadre dans la SceneView.</summary>
    public enum FrameGizmoMode
    {
        Move = 0,
        Rotate = 1,
        Scale = 2
    }

    /// <summary>
    /// Réglages d'une projection UV. Classe sérialisable : persiste avec la
    /// fenêtre éditeur.
    ///
    /// Le "cadre" (frame) est une boîte orientée définissant l'espace de
    /// projection : un point à l'intérieur du cadre est ramené dans le cube
    /// normalisé [-0.5, 0.5]^3. Planaire / box / cylindrique / sphérique
    /// interprètent ce cube selon leur géométrie propre. La projection
    /// Triplanar ignore le cadre et utilise la bounding box locale du mesh.
    /// </summary>
    [System.Serializable]
    public class UVProjectionSettings
    {
        public UVProjectionType type = UVProjectionType.Box;

        [Tooltip("Centre du cadre de projection, en espace local du mesh.")]
        public Vector3 frameCenter = Vector3.zero;

        [Tooltip("Rotation du cadre (angles d'Euler), en espace local du mesh.")]
        public Vector3 frameEuler = Vector3.zero;

        [Tooltip("Dimensions du cadre (étendue totale).")]
        public Vector3 frameSize = Vector3.one;

        [Tooltip("Multiplicateur appliqué aux UV (répétition de la texture).")]
        public Vector2 tiling = Vector2.one;

        [Tooltip("Décalage appliqué aux UV.")]
        public Vector2 offset = Vector2.zero;

        [Range(-180f, 180f)]
        [Tooltip("Rotation des UV dans le plan, autour de (0.5, 0.5), en degrés.")]
        public float rotation = 0f;

        public FrameGizmoMode gizmoMode = FrameGizmoMode.Move;

        /// <summary>Vrai si la projection utilise le cadre éditable (gizmo).</summary>
        public bool UsesEditableFrame => type != UVProjectionType.Triplanar;

        Vector3 FrameSizeSafe => new Vector3(
            Mathf.Max(1e-4f, frameSize.x),
            Mathf.Max(1e-4f, frameSize.y),
            Mathf.Max(1e-4f, frameSize.z));

        /// <summary>Matrice du cadre éditable : cube [-0.5,0.5]^3 -> boîte orientée.</summary>
        public Matrix4x4 FrameMatrix =>
            Matrix4x4.TRS(frameCenter, Quaternion.Euler(frameEuler), FrameSizeSafe);

        /// <summary>
        /// Matrice mesh-local -> espace cadre normalisé, pour le canal demandé.
        /// Pour Triplanar, le cadre est dérivé de la bounding box du mesh.
        /// </summary>
        public float4x4 MeshToFrameMatrix(Bounds meshBounds)
        {
            Matrix4x4 frame;
            if (type == UVProjectionType.Triplanar)
            {
                Vector3 size = meshBounds.size;
                if (size.x < 1e-4f) size.x = 1f;
                if (size.y < 1e-4f) size.y = 1f;
                if (size.z < 1e-4f) size.z = 1f;
                frame = Matrix4x4.TRS(meshBounds.center, Quaternion.identity, size);
            }
            else
            {
                frame = FrameMatrix;
            }

            return ToFloat4x4(frame.inverse);
        }

        /// <summary>Cale le cadre éditable sur une bounding box.</summary>
        public void FitToBounds(Bounds b)
        {
            frameCenter = b.center;
            frameEuler = Vector3.zero;
            frameSize = b.size == Vector3.zero ? Vector3.one : b.size;
        }

        /// <summary>Garantit des champs cohérents après désérialisation.</summary>
        public void Validate()
        {
            if (Mathf.Abs(tiling.x) < 1e-4f) tiling.x = 1f;
            if (Mathf.Abs(tiling.y) < 1e-4f) tiling.y = 1f;
            frameSize = FrameSizeSafe;
            rotation = Mathf.Clamp(rotation, -180f, 180f);
        }

        /// <summary>Copie profonde des réglages.</summary>
        public UVProjectionSettings Clone()
        {
            var c = new UVProjectionSettings();
            c.CopyFrom(this);
            return c;
        }

        /// <summary>Recopie tous les champs depuis une autre instance.</summary>
        public void CopyFrom(UVProjectionSettings other)
        {
            if (other == null)
                return;
            type = other.type;
            frameCenter = other.frameCenter;
            frameEuler = other.frameEuler;
            frameSize = other.frameSize;
            tiling = other.tiling;
            offset = other.offset;
            rotation = other.rotation;
            gizmoMode = other.gizmoMode;
        }

        static float4x4 ToFloat4x4(Matrix4x4 m) => new float4x4(
            m.m00, m.m01, m.m02, m.m03,
            m.m10, m.m11, m.m12, m.m13,
            m.m20, m.m21, m.m22, m.m23,
            m.m30, m.m31, m.m32, m.m33);
    }
}
