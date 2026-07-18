using UnityEngine;

namespace Ombrage.Modules.Hook
{
	/// <summary>
	/// Contrainte de corde 2-corps, à SENS UNIQUE (tire, ne pousse jamais), résolue en impulsions
	/// au niveau vitesse + rappel positionnel (Baumgarte).
	///
	/// - Arbitrage Newtonien (req #4) : l'effort se répartit par masse EFFECTIVE (linéaire + angulaire)
	///   au point d'application → un objet léger vient au véhicule, un lourd tracte le véhicule.
	/// - Seuil (D) : au-delà de <see cref="HookModule.pullMassThreshold"/>, l'ancre dynamique est
	///   traitée comme immobile (le véhicule est tracté vers elle) ; en dessous = vrai 2-corps.
	/// - Couple (req B) : l'impulsion est appliquée au MUSEAU / point d'ancre (hors centre de masse)
	///   → la caisse pique/pivote, ce qui permet la correction de trajectoire par les roues.
	///
	/// Étape 2 : segment DIRECT museau→ancre (aucun enroulement). La signature est déjà segmentaire
	/// pour que l'étape 3 (poulie) réutilise ce solveur brin par brin. Lit son tuning sur le module.
	/// </summary>
	public class RopeConstraintSolver
	{
		private const float Baumgarte = 0.2f; // rappel positionnel (0 = aucun, 1 = agressif)

		private readonly HookModule hook;

		public RopeConstraintSolver(HookModule hook)
		{
			this.hook = hook;
		}

		public void Solve(Rigidbody car, Vector3 carEnd, HookAnchor anchor, Vector3 anchorEnd, float ropeLength, float dt)
		{
			if (car == null || anchor == null) return;

			Vector3 delta = anchorEnd - carEnd;
			float dist = delta.magnitude;
			if (dist < 1e-4f) return;
			Vector3 dir = delta / dist; // véhicule → ancre

			float overshoot = dist - ropeLength;
			if (overshoot <= 0f) return; // corde molle → aucune force (sens unique)

			// Seuil Newtonien : l'ancre ne "cède" que si dynamique ET plus légère que le seuil.
			bool anchorMovable = anchor.kind == HookAnchor.BodyKind.Dynamic
								 && anchor.body != null
								 && anchor.body.mass < hook.pullMassThreshold;

			// Vitesse de séparation aux points (rotation incluse).
			Vector3 vCar = car.GetPointVelocity(carEnd);
			Vector3 vAnchor = anchorMovable ? anchor.body.GetPointVelocity(anchorEnd) : Vector3.zero;
			float sep = Vector3.Dot(vAnchor - vCar, dir); // > 0 = la corde s'allonge

			// Masse effective au point (linéaire + angulaire).
			float k = PointEffectiveMass(car, carEnd, dir);
			if (anchorMovable) k += PointEffectiveMass(anchor.body, anchorEnd, dir);
			if (k <= 1e-8f) return;

			// Rappel positionnel, adouci par ropeSoftness (1 = corde molle, aucun rappel).
			float bias = Baumgarte * (1f - hook.ropeSoftness) * overshoot / Mathf.Max(dt, 1e-5f);

			float lambda = (sep + bias) / k; // impulsion scalaire
			if (lambda <= 0f) return;        // ne pousse jamais

			// Clamp par tension max (accélération-équivalente sur le véhicule).
			float maxImpulse = hook.maxTension * car.mass * dt;
			if (maxImpulse > 0f) lambda = Mathf.Min(lambda, maxImpulse);

			Vector3 impulse = lambda * dir;
			car.AddForceAtPosition(impulse, carEnd, ForceMode.Impulse);
			if (anchorMovable)
				anchor.body.AddForceAtPosition(-impulse, anchorEnd, ForceMode.Impulse);
		}

		/// <summary>Masse effective d'un corps au point 'p' le long de 'dir' (linéaire + angulaire).</summary>
		private static float PointEffectiveMass(Rigidbody rb, Vector3 p, Vector3 dir)
		{
			float w = 1f / rb.mass;
			Vector3 r = p - rb.worldCenterOfMass;
			Vector3 rxd = Vector3.Cross(r, dir);
			Vector3 angular = Vector3.Cross(WorldInvInertiaMul(rb, rxd), r);
			return w + Vector3.Dot(angular, dir);
		}

		/// <summary>Multiplie un vecteur par l'inverse du tenseur d'inertie exprimé EN MONDE.</summary>
		private static Vector3 WorldInvInertiaMul(Rigidbody rb, Vector3 v)
		{
			Quaternion q = rb.rotation * rb.inertiaTensorRotation;
			Vector3 local = Quaternion.Inverse(q) * v;
			Vector3 it = rb.inertiaTensor;
			local.x = it.x > 1e-8f ? local.x / it.x : 0f;
			local.y = it.y > 1e-8f ? local.y / it.y : 0f;
			local.z = it.z > 1e-8f ? local.z / it.z : 0f;
			return q * local;
		}
	}
}
