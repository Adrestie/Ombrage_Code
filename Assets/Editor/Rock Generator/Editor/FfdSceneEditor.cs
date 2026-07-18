using UnityEditor;
using UnityEngine;
using Ombrage.Tools.Core.Settings;

namespace Ombrage.Tools.Editor
{
    /// <summary>
    /// Draws the free-form deformation lattice (cage wireframe + draggable control points)
    /// in the Scene view, and writes control-point drags back into the FFD parameters.
    /// </summary>
    public static class FfdSceneEditor
    {
        static readonly Color CageColor = new Color(0.45f, 0.8f, 1f, 0.9f);
        static readonly Color PointColor = new Color(1f, 0.85f, 0.2f, 1f);

        /// <summary>
        /// Draws and edits the lattice for a preview object placed at <paramref name="owner"/>.
        /// <paramref name="box"/> is the lattice box in the owner's local space. Returns
        /// true when a control point was dragged this event (caller should regenerate).
        /// </summary>
        public static bool DrawAndEdit(Transform owner, Bounds box, FfdParameters ffd, Object undoTarget)
        {
            if (owner == null || ffd == null || !ffd.enabled)
                return false;

            Vector3Int res = ffd.ClampedResolution;
            int count = res.x * res.y * res.z;
            if (ffd.controlPointOffsets == null || ffd.controlPointOffsets.Length != count)
                return false;

            // World-space position of every control point.
            var world = new Vector3[count];
            for (int z = 0; z < res.z; z++)
            for (int y = 0; y < res.y; y++)
            for (int x = 0; x < res.x; x++)
            {
                int index = Flatten(x, y, z, res);
                Vector3 local = RestPosition(box, res, x, y, z) + ffd.controlPointOffsets[index];
                world[index] = owner.TransformPoint(local);
            }

            DrawCage(world, res);

            bool changed = false;
            for (int index = 0; index < count; index++)
            {
                Handles.color = PointColor;
                float size = HandleUtility.GetHandleSize(world[index]) * 0.08f;

                EditorGUI.BeginChangeCheck();
                Vector3 moved = Handles.FreeMoveHandle(
                    world[index], size, Vector3.zero, Handles.SphereHandleCap);
                if (!EditorGUI.EndChangeCheck())
                    continue;

                if (undoTarget != null)
                    Undo.RecordObject(undoTarget, "Move FFD Control Point");

                Vector3 newLocal = owner.InverseTransformPoint(moved);
                ToGrid(index, res, out int gx, out int gy, out int gz);
                ffd.controlPointOffsets[index] = newLocal - RestPosition(box, res, gx, gy, gz);
                changed = true;
            }

            return changed;
        }

        static int Flatten(int x, int y, int z, Vector3Int res)
            => x + y * res.x + z * res.x * res.y;

        static void ToGrid(int index, Vector3Int res, out int x, out int y, out int z)
        {
            x = index % res.x;
            y = (index / res.x) % res.y;
            z = index / (res.x * res.y);
        }

        static Vector3 RestPosition(Bounds box, Vector3Int res, int x, int y, int z)
        {
            var t = new Vector3(
                x / (float)(res.x - 1),
                y / (float)(res.y - 1),
                z / (float)(res.z - 1));
            return box.min + Vector3.Scale(t, box.size);
        }

        static void DrawCage(Vector3[] world, Vector3Int res)
        {
            Handles.color = CageColor;
            for (int z = 0; z < res.z; z++)
            for (int y = 0; y < res.y; y++)
            for (int x = 0; x < res.x; x++)
            {
                int index = Flatten(x, y, z, res);
                if (x + 1 < res.x)
                    Handles.DrawLine(world[index], world[Flatten(x + 1, y, z, res)]);
                if (y + 1 < res.y)
                    Handles.DrawLine(world[index], world[Flatten(x, y + 1, z, res)]);
                if (z + 1 < res.z)
                    Handles.DrawLine(world[index], world[Flatten(x, y, z + 1, res)]);
            }
        }
    }
}
