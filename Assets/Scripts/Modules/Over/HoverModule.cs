using UnityEngine;

namespace Ombrage.Modules.Hover
{
    public class HoverModule : BaseModule
    {
        [Header("Hover")]
        public float hoverHeight = 1.5f;
        public float hoverForce = 50f;
        public float hoverDamping = 5f;
        public LayerMask groundLayer = ~0;

        [Header("Stabilisation")]
        public float levelingTorque = 20f;
        public float angularDamping = 5f;

        [Header("Movement")]
        public float thrustForce = 3000f;
        public float maxSpeed = 60f;
        public float moveDamping = 0.8f;

        [Header("Raycast")]
        public int rayCount = 4;
        public float raySpreadRadius = 1.5f;
        public float rayOriginHeight = 1f;

        [HideInInspector]
        public Rigidbody body;

        private RCC3.RCC_CarControllerV3 carController;
        private RCC3.RCC_WheelCollider[] wheelColliders;
        private Vector3[] rayOrigins;
        private bool hovering;

        protected override void Start()
        {
            base.Start();
            body = GetComponent<Rigidbody>();
            carController = GetComponent<RCC3.RCC_CarControllerV3>();
            BuildRayOrigins();
            Activate();
        }

        public override void OnUse()
        {
            if (!hovering && ConsumeEnergy(energyCost))
            {
                Activate();
            }
            else if (hovering)
            {
                Deactivate();
            }
        }

        private void Activate()
        {
            hovering = true;
            body.linearDamping = moveDamping;
            carController.canControl = false;

            wheelColliders = carController.allWheelColliders;
            foreach (var wc in wheelColliders)
            {
                wc.wheelCollider.motorTorque = 0f;
                wc.wheelCollider.brakeTorque = 0f;
                wc.wheelCollider.steerAngle = 0f;
                wc.wheelCollider.enabled = false;
            }
        }

        private void Deactivate()
        {
            //hovering = false;
            //body.linearDamping = 0.05f;
            //carController.canControl = true;

            //if (wheelColliders != null)
            //{
            //    foreach (var wc in wheelColliders)
            //        wc.wheelCollider.enabled = true;
            //}
        }

        private void FixedUpdate()
        {
            if (!hovering) return;

            ApplyHover();
            ApplyMovement();
        }

        private void ApplyHover()
        {
            for (int i = 0; i < rayOrigins.Length; i++)
            {
                Vector3 origin = body.transform.TransformPoint(rayOrigins[i]);

                float targetDist = hoverHeight + rayOriginHeight;
                if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, targetDist * 3f, groundLayer))
                {
                    float error = targetDist - hit.distance;
                    float upVel = body.linearVelocity.y;
                    float force = error * hoverForce - upVel * hoverDamping;
                    Vector3 groundPoint = new Vector3(origin.x, origin.y - rayOriginHeight, origin.z);
                    body.AddForceAtPosition(Vector3.up * force, groundPoint, ForceMode.Acceleration);
                }
            }

            // Keep the body level — cancel tilt without affecting yaw
            Vector3 projectedUp = Vector3.ProjectOnPlane(body.transform.up, Vector3.up);
            Vector3 correctionTorque = Vector3.Cross(body.transform.up, Vector3.up) * levelingTorque;
            body.AddTorque(correctionTorque - body.angularVelocity * angularDamping, ForceMode.Acceleration);
        }

        private void ApplyMovement()
        {
            /*
            if (!ConsumeEnergy(thrustForce))
            {
                Deactivate();
                return;
            }
            */

            float speed = body.linearVelocity.magnitude * 3.6f;
            if (speed >= maxSpeed) return;

            float gas = inputsManager.gasInput();
            float brake = inputsManager.brakeInput();
            float steer = inputsManager.steerInput();

            float forward = gas - brake;
            Vector3 moveDir = body.transform.forward * forward + body.transform.right * steer;

            if (moveDir.sqrMagnitude > 0.001f)
            {
                body.AddForce(moveDir.normalized * thrustForce, ForceMode.Acceleration);
            }
        }

        private void BuildRayOrigins()
        {
            rayOrigins = new Vector3[rayCount];
            for (int i = 0; i < rayCount; i++)
            {
                float angle = (360f / rayCount) * i * Mathf.Deg2Rad;
                rayOrigins[i] = new Vector3(
                    Mathf.Cos(angle) * raySpreadRadius,
                    rayOriginHeight,
                    Mathf.Sin(angle) * raySpreadRadius
                );
            }
        }

        private void OnDrawGizmosSelected()
        {
            BuildRayOrigins();

            Gizmos.color = Color.cyan;
            for (int i = 0; i < rayOrigins.Length; i++)
            {
                Vector3 origin = transform.TransformPoint(rayOrigins[i]);
                Debug.Log(origin);
                Gizmos.DrawLine(origin, origin + Vector3.down * (hoverHeight + rayOriginHeight) * 3f);
            }
        }
    }
}
