using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace RCC3
{
	[AddComponentMenu("BoneCracker Games/Realistic Car Controller V3/Wheel Collider")]
	[RequireComponent(typeof(WheelCollider))]
	public class RCC_WheelCollider : MonoBehaviour
	{
		private RCC_CarControllerV3 carController;

		private Rigidbody carRigid;

		private WheelCollider _wheelCollider;

		private List<RCC_WheelCollider> allWheelColliders = new List<RCC_WheelCollider>();

		public Transform wheelModel;

		private float wheelRotation;

		public float camber;

		private PhysicsMaterial groundMaterial;

		internal float steerAngle;

		internal bool isGrounded;

		internal float totalSlip;

		internal float rpm;

		internal float wheelRPMToSpeed;

		internal float wheelTemparature;

		private RCC_Skidmarks skidmarks;

		private float startSlipValue = 0.25f;

		private int lastSkidmark = -1;

		private float wheelSlipAmountSideways;

		private float wheelSlipAmountForward;

		private float orgSidewaysStiffness = 1f;

		private float orgForwardStiffness = 1f;

		public WheelFrictionCurve forwardFrictionCurve;

		public WheelFrictionCurve sidewaysFrictionCurve;

		private AudioSource audioSource;

		private AudioClip audioClip;

		private List<ParticleSystem> allWheelParticles = new List<ParticleSystem>();

		private ParticleSystem.EmissionModule emission;

		internal float tractionHelpedSidewaysStiffness = 1f;

		private float minForwardStiffness = 0.75f;

		private float maxForwardStiffness = 1f;

		private float minSidewaysStiffness = 0.75f;

		private float maxSidewaysStiffness = 1f;

		public float sidewaysExtremumSlip = 0.25f;

		public float sidewaysExtremumValue = 1.0f;

		public float sidewaysAsymptotreSlip = 0.5f;

		public float sidewaysAsymptoteValue = 0.75f;

		public float sidewaysStiffness = 1.0f;

		public WheelCollider wheelCollider
		{
			get
			{
				if (_wheelCollider == null)
				{
					_wheelCollider = GetComponent<WheelCollider>();
				}
				return _wheelCollider;
			}
			set
			{
				_wheelCollider = value;
			}
		}

		private RCC_GroundMaterials physicsMaterials
		{
			get
			{
				return RCC_GroundMaterials.Instance;
			}
		}

		private RCC_GroundMaterials.GroundMaterialFrictions[] physicsFrictions
		{
			get
			{
				return RCC_GroundMaterials.Instance.frictions;
			}
		}

		private void Start()
		{
			carController = GetComponentInParent<RCC_CarControllerV3>();
			carRigid = carController.GetComponent<Rigidbody>();
			wheelCollider = GetComponent<WheelCollider>();
			allWheelColliders = carController.allWheelColliders.ToList();
			allWheelColliders.Remove(this);
			if ((bool)Object.FindAnyObjectByType(typeof(RCC_Skidmarks)))
			{
				skidmarks = Object.FindAnyObjectByType(typeof(RCC_Skidmarks)) as RCC_Skidmarks;
			}
			else
			{
				skidmarks = Object.Instantiate(RCC_Settings.Instance.skidmarksManager, Vector3.zero, Quaternion.identity);
			}
			wheelCollider.mass = carRigid.mass / 20f;
			forwardFrictionCurve = wheelCollider.forwardFriction;
			sidewaysFrictionCurve = wheelCollider.sidewaysFriction;
			camber = ((!(this == carController.FrontLeftWheelCollider) && !(this == carController.FrontRightWheelCollider)) ? carController.rearCamber : carController.frontCamber);
			switch (RCC_Settings.Instance.behaviorType)
			{
				case RCC_Settings.BehaviorType.SemiArcade:
					forwardFrictionCurve = SetFrictionCurves(forwardFrictionCurve, 0.2f, 1f, 1f, 1f);
					sidewaysFrictionCurve = SetFrictionCurves(sidewaysFrictionCurve, 0.25f, 1f, 1f, 1f);
					forwardFrictionCurve.stiffness = 1f;
					sidewaysFrictionCurve.stiffness = 1f;
					wheelCollider.forceAppPointDistance = Mathf.Clamp(wheelCollider.forceAppPointDistance, 0.35f, 1f);
					break;
				case RCC_Settings.BehaviorType.Drift:
					forwardFrictionCurve = SetFrictionCurves(forwardFrictionCurve, 0.25f, 1f, 0.8f, 0.75f);
					sidewaysFrictionCurve = SetFrictionCurves(sidewaysFrictionCurve, 0.35f, 1f, 0.5f, 0.75f);
					wheelCollider.forceAppPointDistance = Mathf.Clamp(wheelCollider.forceAppPointDistance, 0.1f, 1f);
					if (carController._wheelTypeChoise == RCC_CarControllerV3.WheelType.FWD)
					{
						Debug.LogError("Current behavior mode is ''Drift'', but your vehicle named " + carController.name + " was FWD. You have to use RWD, AWD, or BIASED to rear wheels. Setting it to *RWD* now. ");
						carController._wheelTypeChoise = RCC_CarControllerV3.WheelType.RWD;
					}
					break;
				case RCC_Settings.BehaviorType.Fun:
					forwardFrictionCurve = SetFrictionCurves(forwardFrictionCurve, 0.2f, 1f, 2f, 1f);
					sidewaysFrictionCurve = SetFrictionCurves(sidewaysFrictionCurve, sidewaysExtremumSlip, sidewaysExtremumValue, sidewaysAsymptotreSlip, sidewaysAsymptoteValue);
					wheelCollider.forceAppPointDistance = Mathf.Clamp(wheelCollider.forceAppPointDistance, 0.75f, 2f);
					break;
				case RCC_Settings.BehaviorType.Racing:
					forwardFrictionCurve = SetFrictionCurves(forwardFrictionCurve, 0.2f, 1f, 0.8f, 0.75f);
					sidewaysFrictionCurve = SetFrictionCurves(sidewaysFrictionCurve, 0.3f, 1f, 0.25f, 0.75f);
					wheelCollider.forceAppPointDistance = Mathf.Clamp(wheelCollider.forceAppPointDistance, 0.25f, 1f);
					break;
				case RCC_Settings.BehaviorType.Simulator:
					forwardFrictionCurve = SetFrictionCurves(forwardFrictionCurve, 0.2f, 1f, 0.8f, 0.75f);
					sidewaysFrictionCurve = SetFrictionCurves(sidewaysFrictionCurve, 0.25f, 1f, 0.5f, 0.75f);
					wheelCollider.forceAppPointDistance = Mathf.Clamp(wheelCollider.forceAppPointDistance, 0.1f, 1f);
					break;
			}
			orgForwardStiffness = forwardFrictionCurve.stiffness;
			orgSidewaysStiffness = sidewaysFrictionCurve.stiffness;
			wheelCollider.forwardFriction = forwardFrictionCurve;
			wheelCollider.sidewaysFriction = sidewaysFrictionCurve;
			for (int i = 0; i < RCC_GroundMaterials.Instance.frictions.Length; i++)
			{
				GameObject gameObject = Object.Instantiate(RCC_GroundMaterials.Instance.frictions[i].groundParticles, base.transform.position, base.transform.rotation);
				emission = gameObject.GetComponent<ParticleSystem>().emission;
				emission.enabled = false;
				gameObject.transform.SetParent(base.transform, false);
				gameObject.transform.localPosition = Vector3.zero;
				gameObject.transform.localRotation = Quaternion.identity;
				allWheelParticles.Add(gameObject.GetComponent<ParticleSystem>());
			}
		}

		private WheelFrictionCurve SetFrictionCurves(WheelFrictionCurve curve, float extremumSlip, float extremumValue, float asymptoteSlip, float asymptoteValue)
		{
			WheelFrictionCurve result = curve;
			result.extremumSlip = extremumSlip;
			result.extremumValue = extremumValue;
			result.asymptoteSlip = asymptoteSlip;
			result.asymptoteValue = asymptoteValue;
			return result;
		}

		private void Update()
		{
			if (!carController.sleepingRigid)
			{
				WheelAlign();
				WheelCamber();
			}
		}

		private void FixedUpdate()
		{
			WheelHit hit;
			isGrounded = wheelCollider.GetGroundHit(out hit);
			steerAngle = wheelCollider.steerAngle;
			rpm = wheelCollider.rpm;
			wheelRPMToSpeed = wheelCollider.rpm * wheelCollider.radius / 2.8f * Mathf.Lerp(1f, 0.75f, hit.forwardSlip) * carRigid.transform.lossyScale.y;
			SkidMarks();
			Frictions();
		}

		public void WheelAlign()
		{
			if (!wheelModel)
			{
				Debug.LogError(base.transform.name + " wheel of the " + carController.transform.name + " is missing wheel model. This wheel is disabled");
				base.enabled = false;
			}
			Vector3 vector = wheelCollider.transform.TransformPoint(wheelCollider.center);
			WheelHit hit;
			wheelCollider.GetGroundHit(out hit);
			RaycastHit hitInfo;
			if (Physics.Raycast(vector, -wheelCollider.transform.up, out hitInfo, (wheelCollider.suspensionDistance + wheelCollider.radius) * base.transform.localScale.y) && hitInfo.transform.gameObject.layer != (int)Mathf.Log(RCC_Settings.Instance.vehicleLayer.value, 2f) && !hitInfo.collider.isTrigger)
			{
				wheelModel.transform.position = hitInfo.point + wheelCollider.transform.up * wheelCollider.radius * base.transform.localScale.y;
				float num = (0f - wheelCollider.transform.InverseTransformPoint(hit.point).y - wheelCollider.radius) / wheelCollider.suspensionDistance;
				Debug.DrawLine(hit.point, hit.point + wheelCollider.transform.up * (hit.force / carRigid.mass), (!((double)num <= 0.0)) ? Color.white : Color.magenta);
				Debug.DrawLine(hit.point, hit.point - wheelCollider.transform.forward * hit.forwardSlip * 2f, Color.green);
				Debug.DrawLine(hit.point, hit.point - wheelCollider.transform.right * hit.sidewaysSlip * 2f, Color.red);
			}
			else
			{
				wheelModel.transform.position = vector - wheelCollider.transform.up * wheelCollider.suspensionDistance * base.transform.localScale.y;
			}
			wheelRotation += wheelCollider.rpm * 6f * Time.deltaTime;
			wheelModel.transform.rotation = wheelCollider.transform.rotation * Quaternion.Euler(wheelRotation, wheelCollider.steerAngle, wheelCollider.transform.rotation.z);
		}

		public void WheelCamber()
		{
			Vector3 euler = ((!(wheelCollider.transform.localPosition.x < 0f)) ? new Vector3(wheelCollider.transform.localEulerAngles.x, wheelCollider.transform.localEulerAngles.y, camber) : new Vector3(wheelCollider.transform.localEulerAngles.x, wheelCollider.transform.localEulerAngles.y, 0f - camber));
			Quaternion localRotation = Quaternion.Euler(euler);
			wheelCollider.transform.localRotation = localRotation;
		}

		private void SkidMarks()
		{
			WheelHit hit;
			wheelCollider.GetGroundHit(out hit);
			wheelSlipAmountSideways = Mathf.Abs(hit.sidewaysSlip);
			wheelSlipAmountForward = Mathf.Abs(hit.forwardSlip);
			totalSlip = wheelSlipAmountSideways / 2f + wheelSlipAmountForward / 2f;
			if ((bool)skidmarks)
			{
				return;
			}
			if (wheelSlipAmountSideways > startSlipValue * 2f || wheelSlipAmountForward > startSlipValue * 2f)
			{
				Vector3 pos = hit.point + 2f * carRigid.linearVelocity * Time.deltaTime;
				if (carRigid.linearVelocity.magnitude > 1f)
				{
					lastSkidmark = skidmarks.AddSkidMark(pos, hit.normal, wheelSlipAmountSideways / 2f + wheelSlipAmountForward / 2f, lastSkidmark);
					wheelTemparature += (wheelSlipAmountSideways / 2f + wheelSlipAmountForward / 2f) / (Time.fixedDeltaTime * 100f * Mathf.Lerp(1f, 5f, wheelTemparature / 150f));
				}
				else
				{
					lastSkidmark = -1;
					wheelTemparature -= Time.fixedDeltaTime * 5f;
				}
			}
			else
			{
				lastSkidmark = -1;
				wheelTemparature -= Time.fixedDeltaTime * 5f;
			}
			wheelTemparature = Mathf.Clamp(wheelTemparature, 0f, 150f);
		}

		private void Frictions()
		{
			WheelHit hit;
			wheelCollider.GetGroundHit(out hit);
			bool flag = false;
			for (int i = 0; i < physicsFrictions.Length; i++)
			{
				if (hit.point != Vector3.zero && hit.collider.sharedMaterial == physicsFrictions[i].groundMaterial)
				{
					flag = true;
					forwardFrictionCurve.stiffness = physicsFrictions[i].forwardStiffness;
					sidewaysFrictionCurve.stiffness = physicsFrictions[i].sidewaysStiffness * tractionHelpedSidewaysStiffness;
					if (RCC_Settings.Instance.behaviorType == RCC_Settings.BehaviorType.Drift)
					{
						Drift(Mathf.Abs(hit.forwardSlip));
					}
					wheelCollider.forwardFriction = forwardFrictionCurve;
					wheelCollider.sidewaysFriction = sidewaysFrictionCurve;
					wheelCollider.wheelDampingRate = physicsFrictions[i].damp;
					emission = allWheelParticles[i].emission;
					audioClip = physicsFrictions[i].groundSound;
				}
			}
			if (!flag && physicsMaterials.useTerrainSplatMapForGroundFrictions)
			{
				for (int j = 0; j < physicsMaterials.terrainSplatMapIndex.Length; j++)
				{
					if (hit.point != Vector3.zero && hit.collider.sharedMaterial == physicsMaterials.terrainPhysicMaterial && TerrainSurface.GetTextureMix(base.transform.position) != null && TerrainSurface.GetTextureMix(base.transform.position)[j] > 0.5f)
					{
						flag = true;
						forwardFrictionCurve.stiffness = physicsFrictions[physicsMaterials.terrainSplatMapIndex[j]].forwardStiffness;
						sidewaysFrictionCurve.stiffness = physicsFrictions[physicsMaterials.terrainSplatMapIndex[j]].sidewaysStiffness * tractionHelpedSidewaysStiffness;
						if (RCC_Settings.Instance.behaviorType == RCC_Settings.BehaviorType.Drift)
						{
							Drift(Mathf.Abs(hit.forwardSlip));
						}
						wheelCollider.forwardFriction = forwardFrictionCurve;
						wheelCollider.sidewaysFriction = sidewaysFrictionCurve;
						wheelCollider.wheelDampingRate = physicsFrictions[physicsMaterials.terrainSplatMapIndex[j]].damp;
						emission = allWheelParticles[physicsMaterials.terrainSplatMapIndex[j]].emission;
						audioClip = physicsFrictions[physicsMaterials.terrainSplatMapIndex[j]].groundSound;
						if (wheelSlipAmountSideways > physicsFrictions[physicsMaterials.terrainSplatMapIndex[j]].slip || wheelSlipAmountForward > physicsFrictions[physicsMaterials.terrainSplatMapIndex[j]].slip * 2f)
						{
							emission.enabled = false;
						}
						else
						{
							emission.enabled = false;
						}
					}
				}
			}
			if (!flag)
			{
				forwardFrictionCurve.stiffness = orgForwardStiffness;
				sidewaysFrictionCurve.stiffness = orgSidewaysStiffness * tractionHelpedSidewaysStiffness;
				if (RCC_Settings.Instance.behaviorType == RCC_Settings.BehaviorType.Drift)
				{
					Drift(Mathf.Abs(hit.forwardSlip));
				}
				wheelCollider.forwardFriction = forwardFrictionCurve;
				wheelCollider.sidewaysFriction = sidewaysFrictionCurve;
				wheelCollider.wheelDampingRate = physicsFrictions[0].damp;
				emission = allWheelParticles[0].emission;
				audioClip = physicsFrictions[0].groundSound;
				if (wheelSlipAmountSideways > physicsFrictions[0].slip || wheelSlipAmountForward > physicsFrictions[0].slip * 2f)
				{
					emission.enabled = false;
				}
				else
				{
					emission.enabled = false;
				}
			}
			for (int k = 0; k < allWheelParticles.Count; k++)
			{
				if (!(wheelSlipAmountSideways > startSlipValue) && !(wheelSlipAmountForward > startSlipValue * 2f))
				{
					emission = allWheelParticles[k].emission;
					emission.enabled = false;
				}
			}
		}

		private void Drift(float forwardSlip)
		{
			Vector3 vector = base.transform.InverseTransformDirection(carRigid.linearVelocity);
			float num = vector.x * vector.x / 200f;
			if (wheelCollider == carController.FrontLeftWheelCollider.wheelCollider || wheelCollider == carController.FrontRightWheelCollider.wheelCollider)
			{
				forwardFrictionCurve.extremumValue = Mathf.Clamp(1f - num, 0.1f, maxForwardStiffness);
				forwardFrictionCurve.asymptoteValue = Mathf.Clamp(0.75f - num / 2f, 0.1f, minForwardStiffness);
			}
			else
			{
				forwardFrictionCurve.extremumValue = Mathf.Clamp(1f - num, 0.5f, maxForwardStiffness);
				forwardFrictionCurve.asymptoteValue = Mathf.Clamp(0.75f - num / 2f, 0.8f, minForwardStiffness);
			}
			if (wheelCollider == carController.FrontLeftWheelCollider.wheelCollider || wheelCollider == carController.FrontRightWheelCollider.wheelCollider)
			{
				sidewaysFrictionCurve.extremumValue = Mathf.Clamp(1f - num / 1f, 0.5f, maxSidewaysStiffness);
				sidewaysFrictionCurve.asymptoteValue = Mathf.Clamp(0.75f - num / 2f, 0.5f, minSidewaysStiffness);
			}
			else
			{
				sidewaysFrictionCurve.extremumValue = Mathf.Clamp(1f - num, 0.5f, maxSidewaysStiffness);
				sidewaysFrictionCurve.asymptoteValue = Mathf.Clamp(0.75f - num / 2f, 0.5f, minSidewaysStiffness);
			}
		}

		private bool isSkidding()
		{
			for (int i = 0; i < allWheelColliders.Count; i++)
			{
				if (allWheelColliders[i].totalSlip > totalSlip)
				{
					return true;
				}
			}
			return false;
		}
	}
}
