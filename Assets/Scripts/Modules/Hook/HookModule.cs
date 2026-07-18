using UnityEngine;

namespace Ombrage.Modules.Hook
{
	/// <summary>
	/// Grappin physique (module véhicule). Unique MonoBehaviour du système (hérite de BaseModule) ;
	/// le reste = classes utilitaires. Tous les réglages sont ICI, groupés par [Header].
	///
	/// Étape 1 : visée caméra → raycast instantané → accroche (convexe / terrain / tag) → corde
	///           LineRenderer collée à l'ancre (mobile incluse) → détache.
	/// Étape 2 : contrainte de corde 2-corps (swing + arbitrage Newtonien de masse), segment DIRECT
	///           museau→ancre (l'enroulement/poulie arrive à l'étape 3). Pas de treuil pour l'instant :
	///           la longueur de corde est figée à l'accroche.
	/// </summary>
	public class HookModule : BaseModule
	{
		private const float MinRopeLength = 0.5f;

		[Header("Références")]
		[Tooltip("Origine physique/visuelle de la corde sur le véhicule (défaut : centre de masse).")]
		public Transform muzzle;
		[Tooltip("Source de visée (défaut : Camera.main).")]
		public Transform aimCamera;

		[Header("Visée")]
		[Tooltip("Portée max du raycast d'accroche (m).")]
		public float maxDistance = 50f;
		[Tooltip("Couches touchables par le raycast d'accroche.")]
		public LayerMask hookMask = ~0;
		[Tooltip("Tag requis pour qu'une surface soit accrochable (vide = aucun filtre de tag).")]
		public string hookableTag = "Hookable";

		[Header("Enroulement / poulie (étape 3)")]
		[Tooltip("Couches considérées comme obstacles pour l'enroulement de la corde.")]
		public LayerMask obstacleMask = 1; // Default
		[Tooltip("Rayon de la corde pour les tests d'obstacle (spherecast).")]
		public float ropeRadius = 0.05f;
		[Tooltip("Nombre max de points d'enroulement.")]
		public int maxNodes = 8;
		[Tooltip("Intervalle (s) entre deux passes de simplification de l'enroulement.")]
		public float obstacleCheckRate = 0.1f;

		[Header("Traction & Swing")]
		[Tooltip("Tension max applicable (accélération-équivalente sur le véhicule, m/s²).")]
		public float maxTension = 60f;
		[Tooltip("Force de contrôle aérien pendant le swing.")]
		public float swingForce = 15f;
		[Range(0f, 1f)]
		[Tooltip("0 = corde parfaitement rigide (inextensible), 1 = corde très souple (ressort).")]
		public float ropeSoftness = 0f;
		[Tooltip("Seuil Newtonien (kg) : un objet plus léger est tracté vers le véhicule ; " +
				 "plus lourd, c'est le véhicule qui est tracté vers lui.")]
		public float pullMassThreshold = 500f;

		[Header("Rendu")]
		[Tooltip("Largeur de la corde (m).")]
		public float ropeWidth = 0.03f;
		[Tooltip("Matériau de la corde (unlit HDRP conseillé). Null = matériau blanc par défaut.")]
		public Material ropeMaterial;

		[Header("Cosmétique")]
		[Tooltip("Durée de l'animation d'extension visuelle de la corde au tir (s).")]
		public float fireVisualDuration = 0.05f;

		[Header("État (lecture seule)")]
		public HookState state = HookState.Idle;
		[Tooltip("Longueur de la corde (m), figée à l'accroche (treuil : plus tard).")]
		public float ropeLength;

		private Rigidbody body;
		private HookAnchor anchor;
		private readonly RopePath path = new RopePath();
		private RopeRenderer ropeRenderer;
		private RopeConstraintSolver constraint;
		private float fireTimer;

		protected override void Start()
		{
			base.Start();
			body = GetComponent<Rigidbody>();

			if (aimCamera == null && Camera.main != null)
				aimCamera = Camera.main.transform;

			constraint = new RopeConstraintSolver(this);

			ropeRenderer = new RopeRenderer();
			ropeRenderer.Initialize(ropeWidth, ropeMaterial);
		}

		public override void OnUse()
		{
			switch (state)
			{
				case HookState.Idle:
					TryShoot();
					break;
				case HookState.Firing:
				case HookState.Attached:
					Detach();
					break;
			}
		}

		private void TryShoot()
		{
			if (aimCamera == null)
				return;
			if (!ConsumeEnergy(energyCost))
				return;

			Ray ray = new Ray(aimCamera.position, aimCamera.forward);
			if (!Physics.Raycast(ray, out RaycastHit hit, maxDistance, hookMask, QueryTriggerInteraction.Ignore))
				return; 

			if (!IsHookable(hit))
				return;

			Attach(hit);
		}

		private bool IsHookable(RaycastHit hit)
		{
			if (!string.IsNullOrEmpty(hookableTag) && !hit.collider.CompareTag(hookableTag))
				return false;

			// Terrain Unity explicitement supporté.
			if (hit.collider is TerrainCollider)
				return true;

			// Sinon : colliders convexes uniquement (mesh concave rejeté).
			if (hit.collider is MeshCollider mesh && !mesh.convex)
				return false;

			return true;
		}

		private void Attach(RaycastHit hit)
		{
			anchor = new HookAnchor(hit);
			path.Clear();

			// Longueur initiale = distance courante → corde tendue mais sans à-coup.
			ropeLength = Mathf.Max((hit.point - MuzzlePosition()).magnitude, MinRopeLength);

			state = HookState.Firing;
			fireTimer = fireVisualDuration;
		}

		private void Detach()
		{
			state = HookState.Idle;
			anchor = null;
			path.Clear();
			ropeRenderer?.Hide();
		}

		private void FixedUpdate()
		{
			if (state == HookState.Idle)
				return;

			// Ancre détruite / support disparu → on lâche proprement.
			if (anchor == null || !anchor.IsValid)
			{
				Detach();
				return;
			}

			Vector3 muzzlePos = MuzzlePosition();

			// L'ancre suit l'objet mobile (espace local) → la corde reste collée.
			path.Rebuild(muzzlePos, anchor.WorldPosition);

			// Contrainte de corde 2-corps sur le segment direct (étape 2).
			constraint?.Solve(body, muzzlePos, anchor, anchor.WorldPosition, ropeLength, Time.fixedDeltaTime);
		}

		private void LateUpdate()
		{
			if (state == HookState.Idle || anchor == null)
				return;

			Vector3 muzzlePos = MuzzlePosition();
			Vector3 anchorPos = anchor.WorldPosition;

			// Animation cosmétique très rapide : la corde "jaillit" du museau vers l'ancre.
			if (state == HookState.Firing)
			{
				fireTimer -= Time.deltaTime;
				float t = fireVisualDuration > 0f
					? 1f - Mathf.Clamp01(fireTimer / fireVisualDuration)
					: 1f;
				ropeRenderer.DrawSegment(muzzlePos, Vector3.Lerp(muzzlePos, anchorPos, t));

				if (fireTimer <= 0f)
					state = HookState.Attached;
				return;
			}

			ropeRenderer.Draw(path.Points);
		}

		private Vector3 MuzzlePosition()
		{
			if (muzzle != null) return muzzle.position;
			if (body != null) return body.worldCenterOfMass;
			return transform.position;
		}

		private void OnDestroy()
		{
			ropeRenderer?.Dispose();
		}

		private void OnDrawGizmosSelected()
		{
			Transform cam = aimCamera != null
				? aimCamera
				: (Camera.main != null ? Camera.main.transform : null);

			if (cam != null)
			{
				Gizmos.color = Color.yellow;
				Gizmos.DrawLine(cam.position, cam.position + cam.forward * maxDistance);
			}

			if (Application.isPlaying && anchor != null && anchor.IsValid)
			{
				Gizmos.color = Color.green;
				Gizmos.DrawSphere(anchor.WorldPosition, 0.15f);
			}
		}
	}
}
