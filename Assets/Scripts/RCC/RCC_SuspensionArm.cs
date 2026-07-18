using UnityEngine;
namespace RCC3
{
	public class RCC_SuspensionArm : MonoBehaviour
	{
		public enum Axis
		{
			X = 0,
			Y = 1,
			Z = 2
		}

		public WheelCollider wheelcollider;

		public Axis axis;

		private Vector3 orgRot;

		private float totalSuspensionDistance;

		public float offsetAngle = 30f;

		public float angleFactor = 150f;

		private void Start()
		{
			orgRot = base.transform.localEulerAngles;
			totalSuspensionDistance = GetSuspensionDistance();
		}

		private void FixedUpdate()
		{
			float num = GetSuspensionDistance() - totalSuspensionDistance;
			base.transform.localEulerAngles = orgRot;
			switch (axis)
			{
				case Axis.X:
					base.transform.Rotate(Vector3.right, num * angleFactor - offsetAngle, Space.Self);
					break;
				case Axis.Y:
					base.transform.Rotate(Vector3.up, num * angleFactor - offsetAngle, Space.Self);
					break;
				case Axis.Z:
					base.transform.Rotate(Vector3.forward, num * angleFactor - offsetAngle, Space.Self);
					break;
			}
		}

		private float GetSuspensionDistance()
		{
			Vector3 pos;
			Quaternion quat;
			wheelcollider.GetWorldPose(out pos, out quat);
			return wheelcollider.transform.InverseTransformPoint(pos).y;
		}
	}
}