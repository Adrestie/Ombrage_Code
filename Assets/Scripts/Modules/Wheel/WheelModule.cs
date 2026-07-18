using Ombrage.Systems.Inputs;
using UnityEngine;

namespace Ombrage.Modules.Wheel
{
    public class WheelModule : BaseModule
    {
		[HideInInspector]
		public RCC3.RCC_CarControllerV3 carController;

		protected override void Start()
        {
            base.Start();
            inputsManager = InputsManager.Instance;

            if (carController == null)
                carController = GetComponent<RCC3.RCC_CarControllerV3>();
        }
		public override void OnUse()
		{
			throw new System.NotImplementedException();
		}

		// Update is called once per frame
		void Update()
        {
            if (inputsManager.gasInput() > 0)
            {
                ConsumeEnergy(carController.engineRPM);
            }
        }
    }
}