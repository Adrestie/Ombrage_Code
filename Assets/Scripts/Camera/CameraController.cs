using Ombrage.Systems.Inputs;
using UnityEngine;
using UnityEngine.InputSystem;

public class CameraController : MonoBehaviour
{
	private InputsManager inputsManager;
	public Transform target;
	public Rigidbody targetRigidbody;

	public bool canRotate;
	public float targetHeight = 1.7f;
	public float distance = 5f;

	public float offsetFromWall = 0.1f;

	public bool reverseVertical;
	public bool reverseHorizontal;

	public float maxDistance = 20f;
	public float minDistance = 0.6f;
	public AnimationCurve distanceFromVelocity = new AnimationCurve();

	public float xSensibility = 1f;
	public float xSpeed = 200f;

	public float ySensibility = 1f;
	public float ySpeed = 200f;

	public float tiltStrength;

	public float tiltLimit;

	public int yMinLimit = -80;
	public int yMaxLimit = 80;
	public int zoomRate = 40;

	public float rotationDampening = 3f;
	public float zoomDampening = 5f;
	public LayerMask collisionLayers = new LayerMask();

	public float xDeg;
	public float yDeg;
	public float zDeg;

	private float zDegLerp;

	private float _smoothXDeg;
	private float _smoothYDeg;

	private float currentDistance;
	private float desiredDistance;
	private float correctedDistance;

	public float timeBeforeReset = 3f;
	public float autoFollowSpeed = 2f;

	private float _timeReset;


	private void OnEnable()
	{
		inputsManager = InputsManager.Instance;
	}

	private void OnDisable()
	{
		inputsManager.DisableCamera();
	}

	private void Start()
	{
		targetRigidbody = target.root.GetComponent<Rigidbody>();
		Vector3 eulerAngles = target.transform.eulerAngles;
		float y = target.eulerAngles.y;
		float y2 = base.transform.eulerAngles.y;
		xDeg = y;
		yDeg = 5f;
		currentDistance = distance;
		desiredDistance = distance;
		correctedDistance = distance;
		_smoothXDeg = xDeg;
		_smoothYDeg = yDeg;
		Quaternion rotation = Quaternion.Euler(yDeg, xDeg, 0f);
		base.transform.rotation = rotation;
	}

	private void Update()
	{
		if (canRotate)
		{
			Vector2 orbit = inputsManager.orbitInput();
			float orbitX = orbit.x * xSensibility;
			float orbitY = orbit.y * ySensibility;

			bool hasInput = Mathf.Abs(orbit.x) > 0.01f || Mathf.Abs(orbit.y) > 0.01f;

			if (hasInput)
			{
				_timeReset = timeBeforeReset;

				if (reverseHorizontal) orbitX = -orbitX;
				if (reverseVertical) orbitY = -orbitY;

				xDeg += orbitX * xSpeed * 0.02f;
				yDeg -= orbitY * ySpeed * 0.02f;
			}
			else
			{
				_timeReset -= Time.deltaTime;
				if (_timeReset <= 0f && target != null)
				{
					float targetYaw = target.eulerAngles.y;
					float deltaX = Mathf.DeltaAngle(xDeg, targetYaw);
					xDeg += deltaX * autoFollowSpeed * Time.deltaTime;

					float defaultPitch = 5f;
					float deltaY = Mathf.DeltaAngle(yDeg, defaultPitch);
					yDeg += deltaY * autoFollowSpeed * Time.deltaTime;
				}
			}

			Vector2 zoomInput = inputsManager.zoomInput();
			desiredDistance -= zoomInput.y * Time.deltaTime * zoomRate * Mathf.Abs(desiredDistance);
		}
	}

	private void LateUpdate()
	{
		if ((bool)target)
		{
			yDeg = ((!(yDeg > 180f)) ? yDeg : (yDeg - 360f));
			yDeg = ClampAngle(yDeg, yMinLimit, yMaxLimit);

			_smoothXDeg = Mathf.LerpAngle(_smoothXDeg, xDeg, Time.deltaTime * rotationDampening);
			_smoothYDeg = Mathf.LerpAngle(_smoothYDeg, yDeg, Time.deltaTime * rotationDampening);

			zDegLerp = ClampAngle(zDegLerp, 0f - tiltLimit, tiltLimit);

			Quaternion quaternion = Quaternion.Euler(_smoothYDeg, _smoothXDeg, zDegLerp);

			correctedDistance = distance +  distanceFromVelocity.Evaluate(targetRigidbody.linearVelocity.magnitude);

			Vector3 vector = new Vector3(0f, 0f - targetHeight, 0f);
			Vector3 end = target.position - (quaternion * Vector3.forward * desiredDistance + vector);
			Vector3 vector2 = new Vector3(target.position.x, target.position.y + targetHeight, target.position.z);
			bool occluded = false;

			RaycastHit hitInfo;

			if (Physics.Linecast(vector2, end, out hitInfo, collisionLayers))
			{
				correctedDistance = Vector3.Distance(vector2, hitInfo.point) - offsetFromWall;
				occluded = true;
			}

			currentDistance = ((occluded && !(correctedDistance > currentDistance)) ? correctedDistance : Mathf.Lerp(currentDistance, correctedDistance, Time.deltaTime * zoomDampening));
			currentDistance = Mathf.Clamp(currentDistance, minDistance, maxDistance);
			end = target.position - (quaternion * Vector3.forward * currentDistance + vector);
			base.transform.rotation = quaternion;
			base.transform.position = end;
		}
	}

	private static float ClampAngle(float angle, float min, float max)
	{
		if (angle < -360f)
		{
			angle += 360f;
		}
		if (angle > 360f)
		{
			angle -= 360f;
		}
		return Mathf.Clamp(angle, min, max);
	}
}
