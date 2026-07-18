using System.Collections.Generic;
using UnityEngine;

namespace Ombrage.Modules.Hook
{
	/// <summary>
	/// Géométrie de la corde : polyligne museau → (pivots d'enroulement) → ancre.
	/// Étape 1 : ligne droite (aucun pivot). L'enroulement/poulie (étape 3) remplira
	/// <see cref="nodes"/> puis rappellera <see cref="Rebuild"/>.
	/// </summary>
	public class RopePath
	{
		private readonly List<RopeNode> nodes = new List<RopeNode>(8);
		private readonly List<Vector3> points = new List<Vector3>(16);

		public IReadOnlyList<RopeNode> Nodes => nodes;
		public IReadOnlyList<Vector3> Points => points;

		/// <summary>Longueur totale du chemin (somme des segments).</summary>
		public float TotalLength { get; private set; }

		/// <summary>Premier pivot côté véhicule vers lequel la tension est redirigée (= ancre si pas d'enroulement).</summary>
		public Vector3 CarSidePivot { get; private set; }

		/// <summary>Dernier pivot côté ancre (= museau si pas d'enroulement).</summary>
		public Vector3 AnchorSidePivot { get; private set; }

		public void Clear()
		{
			nodes.Clear();
			points.Clear();
			TotalLength = 0f;
			CarSidePivot = Vector3.zero;
			AnchorSidePivot = Vector3.zero;
		}

		/// <summary>
		/// Reconstruit la polyligne à partir du museau et de la position d'ancre courants.
		/// Les pivots (<see cref="nodes"/>) sont en espace local → ils suivent les supports mobiles.
		/// </summary>
		public void Rebuild(Vector3 muzzle, Vector3 anchorPos)
		{
			points.Clear();
			points.Add(muzzle);
			for (int i = 0; i < nodes.Count; i++)
				points.Add(nodes[i].WorldPosition);
			points.Add(anchorPos);

			CarSidePivot = points.Count > 1 ? points[1] : anchorPos;
			AnchorSidePivot = points.Count > 1 ? points[points.Count - 2] : muzzle;

			float len = 0f;
			for (int i = 0; i < points.Count - 1; i++)
				len += Vector3.Distance(points[i], points[i + 1]);
			TotalLength = len;
		}
	}
}
