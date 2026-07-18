using UnityEngine;

namespace RCC3
{
public class EssieuHeight : MonoBehaviour
{
	public Transform wheelTarget;

	public Transform EssieuxTarget;

	public float offset;

	public bool isGB;

	private void Start()
	{
	}

	private void Update()
	{
		base.transform.localPosition = new Vector3(base.transform.localPosition.x, wheelTarget.localPosition.y + offset, base.transform.localPosition.z);
		if (isGB)
		{
			base.transform.localPosition = new Vector3(base.transform.localPosition.x, EssieuxTarget.localPosition.y + offset, base.transform.localPosition.z);
			base.transform.localRotation = Quaternion.Euler(0f, wheelTarget.GetComponent<WheelCollider>().steerAngle, 0f);
		}
	}
}
}
