using UnityEngine;

namespace Ombrage.Modules.Hook
{
	public class RopeNode
	{
		public Collider collider;
		public Transform transform;

		public Vector3 LocalPosition;

		public Vector3 WorldPosition =>
			transform != null
				? transform.TransformPoint(LocalPosition)
				: LocalPosition;

		public RopeNode(Collider collider, Vector3 worldPos)
		{
			this.collider = collider;

			transform = this.collider != null && this.collider.attachedRigidbody != null
				? this.collider.attachedRigidbody.transform
				: this.collider != null ? this.collider.transform : null;

			if (transform != null)
				LocalPosition = transform.InverseTransformPoint(worldPos);
			else
				LocalPosition = worldPos;
		}
	}
}
