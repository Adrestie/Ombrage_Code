using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ombrage.Modules.Hook
{
	/// <summary>
	/// Rendu simple de la corde via un LineRenderer sur un GameObject runtime dédié.
	/// Classe utilitaire (PAS un MonoBehaviour) pilotée par <see cref="HookModule"/>.
	/// </summary>
	public class RopeRenderer
	{
		private GameObject go;
		private LineRenderer line;
		private Material ownedMaterial;

		public void Initialize(float width, Material material)
		{
			go = new GameObject("HookRope") { hideFlags = HideFlags.HideAndDontSave };
			line = go.AddComponent<LineRenderer>();
			line.useWorldSpace = true;
			line.widthMultiplier = width;
			line.numCapVertices = 2;
			line.numCornerVertices = 2;
			line.textureMode = LineTextureMode.Tile;
			line.shadowCastingMode = ShadowCastingMode.Off;
			line.receiveShadows = false;

			if (material != null)
			{
				line.material = material;
			}
			else
			{
				// Fallback visible (blanc) pour ne pas dépendre d'un matériau assigné.
				Shader shader = Shader.Find("HDRP/Unlit");
				if (shader != null)
				{
					ownedMaterial = new Material(shader);
					line.material = ownedMaterial;
				}
			}

			line.enabled = false;
		}

		public void DrawSegment(Vector3 a, Vector3 b)
		{
			if (line == null) return;
			line.enabled = true;
			line.positionCount = 2;
			line.SetPosition(0, a);
			line.SetPosition(1, b);
		}

		public void Draw(IReadOnlyList<Vector3> pts)
		{
			if (line == null || pts == null || pts.Count < 2) return;
			line.enabled = true;
			line.positionCount = pts.Count;
			for (int i = 0; i < pts.Count; i++)
				line.SetPosition(i, pts[i]);
		}

		public void Hide()
		{
			if (line != null) line.enabled = false;
		}

		public void Dispose()
		{
			if (go != null) Object.Destroy(go);
			if (ownedMaterial != null) Object.Destroy(ownedMaterial);
			go = null;
			line = null;
			ownedMaterial = null;
		}
	}
}
