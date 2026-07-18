using System;
using Ombrage.StaticSystem;
using UnityEngine;

namespace Ombrage.Systems.BatteryManager
{
[CreateAssetMenu(fileName = "BatteryManager", menuName = "Ombrage/StaticTools/Create New BatteryManager", order = 20)]
public class BatteryManager : StaticSystemBase
{
	public static BatteryManager Instance
	{
		get
		{
			#if UNITY_EDITOR
			 if (!Application.isPlaying)
			{
				AssetHandler.FindAsset("BatteryManager", out _instance, ".asset");
			}
			#endif
			return _instance;
		}
		internal set
		{
			_instance = value;
		}
	}

	private static BatteryManager _instance;
    //Specifics variables here

    public int batteryExtensionSlot = 0;
    public int batteryExtensionAmountPerSlot = 25;

	public int baseBattery = 100;
    public float maximumBattery { get { return baseBattery + (batteryExtensionSlot * batteryExtensionAmountPerSlot); } }
	public float currentBattery;

	//Events
	public event Action<float> OnBatteryUsed;
	public event Action OnBatteryUseDenied;

	public event Action<float> OnBatteryRestored;
	public event Action OnBatteryRestoreDenied;


    public override void Initialize()
	{
		_instance = this;
		base.Init(_instance); 
		currentBattery = maximumBattery;
		//Do specific Initialization here

		Save(_instance);
	}

	public bool CanConsume()
	{
        /// <summary>
        /// Does the module can be used ?
        /// </summary>

        return currentBattery > 0;
	}

	public bool CanRestore(float amount)
	{
		return currentBattery < maximumBattery;
	}

	public bool Consume(float amount)
	{
		if (!CanConsume())
		{
			OnBatteryUseDenied?.Invoke();
			return false;
		}
		else
		{
			currentBattery = Mathf.Clamp(currentBattery - amount, 0, maximumBattery);
			OnBatteryUsed?.Invoke(currentBattery / maximumBattery);
			return true;
		}
	}

	public void Restore(float amount)
	{
		if (!CanRestore(amount))
		{
			OnBatteryRestoreDenied?.Invoke();
		}
		else
		{
			currentBattery = Mathf.Clamp(currentBattery + amount, 0, maximumBattery);
			OnBatteryRestored?.Invoke(currentBattery / maximumBattery);

		}
	}

	public override Tuple<bool, string> Import()
	{
		return Load(this);
	}

	public override Tuple<bool, string> Export()
	{
		return Save(this);
	}
}
}