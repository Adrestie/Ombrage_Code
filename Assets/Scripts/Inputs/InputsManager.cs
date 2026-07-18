using System;

using JetBrains.Annotations;

using Ombrage.StaticSystem;

using UnityEngine;
using UnityEngine.InputSystem;

namespace Ombrage.Systems.Inputs
{
[CreateAssetMenu(fileName = "InputsManager", menuName = "Ombrage/StaticTools/Create New InputsManager", order = 20)]
public class InputsManager : StaticSystemBase
{
	public static InputsManager Instance
	{
		get
		{
			#if UNITY_EDITOR
			 if (!Application.isPlaying)
			{
				AssetHandler.FindAsset("InputsManager", out _instance, ".asset");
			}
			#endif
			return _instance;
		}
		internal set
		{
			_instance = value;
		}
	}

	private static InputsManager _instance;
	private InputSystem_Actions inputActions;

	public event Action OnPrimaryModuleUsed;
	public event Action OnSecondaryModuleUsed;


	public override void Initialize()
	{
		_instance = this;
		base.Init(_instance);

		//Do specific Initialization here
		inputActions = new InputSystem_Actions();
		EnableVehicle();
		EnableCamera();
		inputActions.Vehicle.PrimaryModule.performed += _ => usePrimaryModule();
		inputActions.Vehicle.SecondaryModule.performed += _ => useSecondaryModule();

		Save(_instance);
	}

	public override Tuple<bool, string> Import()
	{
		return Load(this);
	}

	public override Tuple<bool, string> Export()
	{
		return Save(this);
	}
	/// <summary>
	/// VEHICLE INPUTS
	/// </summary>
	public void EnableVehicle()
	{
		inputActions.Vehicle.Enable();
	}

	public bool isVehicleEnabled()
	{
		return inputActions.Vehicle.enabled;
	}

	public void DisableVehicle()
	{
		inputActions.Vehicle.Disable();
	}

	public float gasInput() => inputActions.Vehicle.Throttle.ReadValue<float>();
	public float brakeInput() => inputActions.Vehicle.Brake.ReadValue<float>();
	public float steerInput() => inputActions.Vehicle.Steering.ReadValue<float>();
	public float handbrakeInput() => inputActions.Vehicle.Handbrake.ReadValue<float>();
	public bool boostInput() => inputActions.Vehicle.NOS.IsPressed();
	public float clutchInput() => inputActions.Vehicle.Clutch.ReadValue<float>();

	/// <summary>
	/// CAMERA INPUTS
	/// </summary>

	public void EnableCamera()
	{
		inputActions.Camera.Enable();
	}

	public bool isCameraEnabled()
	{
		return inputActions.Camera.enabled;
	}

	public void DisableCamera()
	{
		inputActions.Camera.Disable();
	}

	public Vector2 orbitInput() => inputActions.Camera.Orbit.ReadValue<Vector2>();
	public Vector2 zoomInput() => inputActions.Camera.Zoom.ReadValue<Vector2>();

	/// <summary>
	/// MODULE INPUTS
	/// </summary>

	public void usePrimaryModule()
	{
		OnPrimaryModuleUsed?.Invoke();
	}

	public void useSecondaryModule()
	{ 
		OnSecondaryModuleUsed?.Invoke(); 
	}

	/// <summary>
	/// MISC
	/// </summary>

	public void Dispose()
	{
		inputActions.Dispose();
	}

}
}