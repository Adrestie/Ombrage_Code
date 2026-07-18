using UnityEngine;
namespace RCC3
{
	public class RCC_LockRot : MonoBehaviour
	{
		private Quaternion orgRotation;

		private void Awake()
		{
			orgRotation = base.transform.localRotation;
		}

		private void Update()
		{
			base.transform.localRotation = orgRotation;
		}
	}
}