using UnityEngine;

namespace Ombrage.Modules.Hook
{
	/// <summary>
	/// Point d'accroche du grappin sur la cible.
	/// Le point est mémorisé en espace LOCAL du support → il suit automatiquement
	/// un objet mobile. Expose aussi la nature du corps (statique / kinematic / dynamique),
	/// sa masse inverse et sa vitesse au point, nécessaires à la contrainte de l'étape 2.
	/// </summary>
	public class HookAnchor
	{
		public enum BodyKind { Static, Kinematic, Dynamic }

		public readonly Collider collider;
		public readonly Transform anchorTransform;
		public readonly Rigidbody body;
		public readonly BodyKind kind;
		public readonly Vector3 localPosition;

		public HookAnchor(RaycastHit hit)
		{
			collider = hit.collider;
			body = hit.rigidbody;

			if (body == null) kind = BodyKind.Static;
			else if (body.isKinematic) kind = BodyKind.Kinematic;
			else kind = BodyKind.Dynamic;

			anchorTransform = body != null ? body.transform : collider.transform;
			localPosition = anchorTransform.InverseTransformPoint(hit.point);
		}

		/// <summary>Faux si le collider (ou son support) a été détruit → le module lâche.</summary>
		public bool IsValid => collider != null && anchorTransform != null;

		/// <summary>Position monde courante du point d'accroche (suit le support mobile).</summary>
		public Vector3 WorldPosition =>
			anchorTransform != null ? anchorTransform.TransformPoint(localPosition) : localPosition;

		/// <summary>Masse inverse pour l'arbitrage : 0 pour statique/kinematic (masse "infinie").</summary>
		public float InvMass => kind == BodyKind.Dynamic && body != null ? 1f / body.mass : 0f;

		/// <summary>Vitesse du point d'accroche (rotation incluse), pour la contrainte 2-corps.</summary>
		public Vector3 WorldVelocity =>
			body != null ? body.GetPointVelocity(WorldPosition) : Vector3.zero;
	}
}
