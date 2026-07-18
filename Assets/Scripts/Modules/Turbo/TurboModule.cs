using System.Collections;
using RCC3;

using Unity.VisualScripting;

using UnityEngine;

namespace Ombrage.Modules.Turbo
{
    public class TurboModule : BaseModule
    {
		[HideInInspector]
		public Rigidbody body;
		[HideInInspector]
		public RCC_CarControllerV3 carController;

		[Header("Turbo")]
		public float turboDuration = 3.0f;
		public float turboKickStrength = 15;
		public float turboAccelerationStrength = 25f;
		public float lockedAngularDamping = 10f;

		[Header("Slip fix")]
		[Tooltip("1 = match exact (annule le frein, 0 traction). >1 = léger sur-régime -> les pneus poussent (risque patinage). Garder dans [1 ; ~1.1].")]
		public float tractionBias = 1f; 

		private float originalAngularDamping = 0f;
		private bool boosting = false;
		private bool initialKickPerformed = false;
		private float boostTimeRemaining = 0f;
		private RCC_WheelCollider[] wheelColliders;

		protected override void Start()
		{
			base.Start();
			body = GetComponent<Rigidbody>();
			carController = GetComponent<RCC_CarControllerV3>();
			wheelColliders = carController.allWheelColliders;
			originalAngularDamping = body.angularDamping;
		}

		public override void OnUse()
		{
			if (isInCooldown || boosting)
				return;

			if (!ConsumeEnergy(energyCost))
				return;

			boosting = true;
			initialKickPerformed = false;

			boostTimeRemaining = turboDuration;
			originalAngularDamping = body.angularDamping;

			cooldown = cooldownDuration;
			StartCoroutine(cooldownCalculation());
		}

		public void FixedUpdate()
		{
			if (!boosting)
				return;

			if (!initialKickPerformed)
			{
				body.AddForce(body.transform.forward * turboKickStrength, ForceMode.VelocityChange);
				SyncWheelRotation();
				initialKickPerformed = true;
			}

			body.AddForce(body.transform.forward * turboAccelerationStrength, ForceMode.Acceleration);

			float t = turboDuration > 0f ? boostTimeRemaining / turboDuration : 0f;
			body.angularDamping = Mathf.Lerp(originalAngularDamping, lockedAngularDamping, t);

			boostTimeRemaining -= Time.fixedDeltaTime;
			if (boostTimeRemaining <= 0f)
				EndBoost();
		}

		private void SyncWheelRotation()
		{
			for (int i = 0; i < wheelColliders.Length; i++)
			{
				WheelCollider w = wheelColliders[i].wheelCollider;
				w.rotationSpeed = 20000;
			}
		}

		private void EndBoost()
		{
			boosting = false;
			body.angularDamping = originalAngularDamping;
		}
	}
}
