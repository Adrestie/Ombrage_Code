using System.Collections;

using Ombrage.Abstractions;
using Ombrage.Systems.BatteryManager;
using Ombrage.Systems.Inputs;

using UnityEngine;
namespace Ombrage.Modules
{
	public abstract class BaseModule : MonoBehaviour, IModule
	{
		// Start is called once before the first execution of Update after the MonoBehaviour is created
		private BatteryManager mBatteryManager;
		[HideInInspector]
		public InputsManager inputsManager;


		public enum ModuleType { Permanent, Primary, Secondary };
		public ModuleType moduleType;
		public bool isEnabled = false;

		public bool isInCooldown = false;
		bool IModule.IsEnabled => isEnabled;
		bool IModule.IsInCooldown => isInCooldown;

		//ENERGY SETTINGS
		public enum DrainMode { Spontaneous, Continuous, Intermittent, Curve };
		public DrainMode drainMode = DrainMode.Spontaneous;
		public float energyCost = 0f;

		//CONSUMPTION SETTINGS (INTERMITTENT MODE)
		public float consumptionInterval = 1.0f;
		private float _consumptionTick = 0.0f;
		public AnimationCurve consumptionCurve = new AnimationCurve();

		//COOLDOWN
		public float cooldownDuration = 10f;
		public float cooldown = 0f;
		public WaitForEndOfFrame waitForEndOfFrame = new WaitForEndOfFrame();

		protected virtual void Start()
		{
			mBatteryManager = BatteryManager.Instance;
			inputsManager = InputsManager.Instance;

			if (moduleType == ModuleType.Primary )
				inputsManager.OnPrimaryModuleUsed += OnUse;
			else if (moduleType == ModuleType.Secondary )
				inputsManager.OnSecondaryModuleUsed += OnUse;
		}

		public bool ConsumeEnergy(float amount)
		{
			isEnabled = mBatteryManager.CanConsume();
			if (!isEnabled)
				return isEnabled;

			switch (drainMode)
			{
				case DrainMode.Spontaneous:
					mBatteryManager.Consume(amount);
					break;
				case DrainMode.Continuous:
					mBatteryManager.Consume(amount * Time.deltaTime);
					break;
				case DrainMode.Intermittent:
					_consumptionTick += Time.deltaTime;
					if (_consumptionTick > consumptionInterval)
					{
						mBatteryManager.Consume(amount);
						_consumptionTick = 0f;
					}
					break;
				case DrainMode.Curve:
					mBatteryManager.Consume(consumptionCurve.Evaluate(amount) * Time.deltaTime);
					break;
			}

			return isEnabled;
		}

		public IEnumerator cooldownCalculation()
		{
			while (cooldown > 0f)
			{
				isInCooldown = true;
				cooldown -= Time.deltaTime;
				yield return waitForEndOfFrame;
			}
			cooldown = 0f;
			isInCooldown = false;
		}

		public abstract void OnUse();

	}
}
