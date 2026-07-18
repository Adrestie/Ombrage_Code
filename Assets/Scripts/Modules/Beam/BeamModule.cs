using UnityEngine;


namespace Ombrage.Modules.Beam
{
    public class BeamModule : BaseModule
    {
        public GameObject leftBeam, rightBeam;
        public LayerMask layerMask;
        private Transform _leftBeam, _rightBeam;
        private Light _leftSpotLight, _rightSpotLight;
        private Transform _cameraTransform;

		public float rotationSpeed = 10f;


		Vector3 DEBUG_beamTarget = Vector3.zero;
		// Start is called once before the first execution of Update after the MonoBehaviour is created
		protected override void Start()
        {
            base.Start();
			_cameraTransform = Camera.main.transform;

			_leftBeam = leftBeam.transform;
            _rightBeam = rightBeam.transform;

            _leftSpotLight = leftBeam.GetComponent<Light>();
            _rightSpotLight = rightBeam.GetComponent<Light>();
        }

        // Update is called once per frame
        void Update()
        {
			if (isEnabled)
			{
				if (ConsumeEnergy(energyCost))
				{
					_leftSpotLight.enabled = _rightSpotLight.enabled = true;
					Vector3 _beamTarget = Vector3.zero;
					Ray ray = new Ray(_cameraTransform.position, _cameraTransform.forward);
					Debug.DrawRay(ray.origin, ray.direction*100);
					if (Physics.Raycast(ray, out RaycastHit hitInfo, 100f, layerMask))
					{
						_beamTarget = hitInfo.point;
					}
					else
					{
						_beamTarget = _cameraTransform.position + _cameraTransform.forward * 100f;
					}
					Quaternion leftTargetRotation = Quaternion.LookRotation(_beamTarget - _leftBeam.position);
					Quaternion rightTargetRotation =Quaternion.LookRotation(_beamTarget - _rightBeam.position);

					_leftBeam.rotation = Quaternion.Slerp(_leftBeam.rotation, leftTargetRotation, rotationSpeed * Time.deltaTime);

					_rightBeam.rotation = Quaternion.Slerp(_rightBeam.rotation, rightTargetRotation, rotationSpeed * Time.deltaTime);
				}
				else
				{
					_leftSpotLight.enabled = _rightSpotLight.enabled = false;
				}
			}
		}

		public override void OnUse()
		{
			isEnabled = !isEnabled;
			if (!isEnabled)
				_leftSpotLight.enabled = _rightSpotLight.enabled = false;
		}

		private void OnDrawGizmos()
		{
            Gizmos.DrawSphere(DEBUG_beamTarget, 1f);
		}
	}
}
