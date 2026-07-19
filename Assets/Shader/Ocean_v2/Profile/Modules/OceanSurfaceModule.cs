// OceanSurfaceModule.cs  (Ocean_v2)
// Module SURFACE — première surface opaque rendue en DEFERRED (GBuffer), tessellation adaptative
// gatée distance, normales analytiques (lues des cascades du spectre), passe MotionVectors native.
//
// Architecture (pattern herbe/terrain) : ce ScriptableObject est PUR DATA ; tout l'état runtime
// (GameObject + mesh grille FIXE, matériau, coordinator MV) est détenu par OceanSystem via SetRuntime
// dans un OceanSurfaceRuntime non sérialisé.
//
// Reserrages de scope APPROUVÉS :
//   - grille UNIFORME FIXE world-locked (suivi caméra / extension monde = pivot clipmap différé) ;
//   - tampon T-1 détenu par le coordinator, copie faite dans PreSimulate AVANT l'évolution du spectre,
//     cadence = Time.frameCount (spectre byte-à-byte intact).
//
// Contrats anti-bug :
//   n°1 : tous les globaux (_OceanDispPrev*, _OceanMVValid) poussés via ctx.globals (restaurés au Teardown) ;
//   n°2 : normales ANALYTIQUES depuis les pentes des cascades (jamais de différences finies) — shader ;
//   n°3 : aucune normalisation couplée à l'amplitude introduite ici.
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Ombrage.OceanFeatures
{
    [OceanModuleMenu("Rendering/Surface")]
    public class OceanSurfaceModule : OceanFeatureModule
    {
        // ── Géométrie de base (grille FIXE) ─────────────────────────────────
        [Header("Maillage de base (grille UNIFORME FIXE world-locked)")]
        [Tooltip("Demi-étendue de la grille (m). Doit couvrir généreusement la scène de test : aucun suivi caméra pour l'instant (clipmap différé).")]
        [Min(1f)] public float gridExtent = 500f;

        [Tooltip("Résolution de la grille de base (segments par côté). La densité fine est gérée par la tessellation hardware, pas ici.")]
        [Range(2, 254)] public int baseResolution = 100;

        // ── Tessellation adaptative gatée distance (quantifiée, MV-stable) ───
        // Valeurs à OVERRIDE (niveau 2, cf. Reflection). Décoché = défaut ; cocher = saisie. Clamp en
        // OnValidate. Les paramètres de STRUCTURE/allocation (gridExtent, baseResolution, foamResolution)
        // restent des champs simples — comme un choix de résolution, ce n'est pas une « valeur » à surcharger.
        [Header("Tessellation (gatée distance, quantifiée)")]
        [Tooltip("Facteur de tessellation MAX (au plus près). Limite hardware = 64.")]
        public OceanFloatParameter maxTessFactor = new OceanFloatParameter(32f);

        [Tooltip("Distance (m) en deçà de laquelle la tessellation est maximale.")]
        public OceanFloatParameter tessMinDist = new OceanFloatParameter(20f);

        [Tooltip("Distance (m) au-delà de laquelle le facteur retombe à 1.0 (tessellation OFF, coût hull/domain fixe conservé).")]
        public OceanFloatParameter tessMaxDist = new OceanFloatParameter(250f);

        [Tooltip("Nombre de paliers discrets de quantification du facteur (STABILITÉ MV : entre deux franchissements de palier le facteur est constant frame-à-frame).")]
        public OceanIntParameter tessQuantLevels = new OceanIntParameter(8);

        [Tooltip("Pas (m) de snap de la position caméra de référence côté shader (STABILITÉ MV : la distance de gating ne varie pas en continu).")]
        public OceanFloatParameter refCamSnap = new OceanFloatParameter(2f);

        // ── Bounds / déplacement ────────────────────────────────────────────
        [Header("Bounds (recalculés à chaud)")]
        [Tooltip("Hauteur de vague max attendue (m) — borne Y des bounds. Le déplacement horizontal est AUTO-DÉRIVÉ des cascades (pas de champ manuel piégeux).")]
        public OceanFloatParameter maxWaveHeight = new OceanFloatParameter(6f);

        [Tooltip("Marge de sécurité multiplicative appliquée aux bounds (évite le culling des crêtes en vue rasante pendant le calibrage à chaud).")]
        public OceanFloatParameter boundsSafetyScale = new OceanFloatParameter(1.25f);

        // ── Apparence : couleur de base = REPLI quand l'absorption est absente/inactive ;
        //    (réflexions et sous-marin gérés par leurs modules dédiés) ──
        [Header("Apparence (_BaseColor = repli si module Absorption inactif)")]
        public OceanColorParameter baseColor = new OceanColorParameter(new Color(0.03f, 0.10f, 0.16f, 1f));
        public OceanFloatParameter smoothness = new OceanFloatParameter(0.92f);
        public OceanFloatParameter metallic = new OceanFloatParameter(0f);

        // ── Écume (feature du module surface) : carte world-locked ──
        [Header("Écume (crêtes)")]
        [Tooltip("Active l'écume (carte world-locked : couverture + persistance). OFF = surface sans écume (branche uniforme côté shader, zéro variant).")]
        public OceanBoolParameter foamEnabled = new OceanBoolParameter(true);

        [Tooltip("Point de déferlante ε sur le Jacobien (J=1 : surface plane ; J<1 = repli aux crêtes). Couverture = P(J < ε) : plus HAUT = écume plus tôt/étendue ; plus bas = seulement les plis les plus marqués.")]
        public OceanFloatParameter jacobianThreshold = new OceanFloatParameter(0.97f);

        [Tooltip("Vitesse de dissipation de la traînée d'écume (s⁻¹). 0 = écume tenue tant que la crête existe ; plus haut = disparaît vite.")]
        public OceanFloatParameter foamFadeRate = new OceanFloatParameter(0.5f);

        [Tooltip("Résolution de la carte d'écume world-locked (texels/côté). DÉCOUPLÉE de Master Tile Length : m/texel = 2·gridExtent / résolution. Coût GPU ∝ résolution².")]
        public int foamResolution = 1024;

        [Tooltip("Compute de la carte d'écume (OceanFoam.compute). Auto-résolu en éditeur si vide — À SÉRIALISER dans le profil pour le build (même caveat que les compute du spectre).")]
        public ComputeShader foamCompute;

        // Douceur du seuil (anti-escalier), constante — pas un slider.
        const float kFoamSoftness = 0.03f;

        [Header("Shader / matériau (auto-résolu si vide)")]
        [Tooltip("Matériau de surface. Si vide, un matériau est créé à partir du shader Custom/HDRP/OceanSurface.")]
        public Material surfaceMaterialOverride;

        public override bool WantsContinuousRepaint => true;   // surface animée chaque frame

        const string kShaderName = "Custom/HDRP/OceanSurface";
        const string kShaderPath = "Assets/Shader/Ocean_v2/Shaders/OceanSurface.shader";

        // ── Identifiants de propriétés MATÉRIAU (UnityPerMaterial) ──────────
        static readonly int P_BaseColor   = Shader.PropertyToID("_BaseColor");
        static readonly int P_Smoothness  = Shader.PropertyToID("_Smoothness");
        static readonly int P_Metallic    = Shader.PropertyToID("_Metallic");
        // Paramètres de tessellation HDRP natifs (présents dans notre cbuffer UnityPerMaterial) :
        static readonly int P_TessFactor       = Shader.PropertyToID("_TessellationFactor");
        static readonly int P_TessMinDistHDRP  = Shader.PropertyToID("_TessellationFactorMinDistance");
        static readonly int P_TessMaxDistHDRP  = Shader.PropertyToID("_TessellationFactorMaxDistance");
        static readonly int P_TessTriSize      = Shader.PropertyToID("_TessellationFactorTriangleSize");
        static readonly int P_TessShape        = Shader.PropertyToID("_TessellationShapeFactor");
        static readonly int P_TessBackCull     = Shader.PropertyToID("_TessellationBackFaceCullEpsilon");
        // Paramètres OCÉAN (gating quantifié calculé côté shader, cf. OceanSurfaceTessellation.hlsl) :
        static readonly int P_OceanTessMin     = Shader.PropertyToID("_OceanTessMinDist");
        static readonly int P_OceanTessMax     = Shader.PropertyToID("_OceanTessMaxDist");
        static readonly int P_OceanTessLevels  = Shader.PropertyToID("_OceanTessQuantLevels");
        static readonly int P_OceanRefCamSnap  = Shader.PropertyToID("_OceanRefCamSnap");
        static readonly int P_OceanMaxDisp     = Shader.PropertyToID("_OceanMaxDisplacement");
        // Arrays de déplacement COURANTS publiés par le spectre (lus en lecture seule pour la garde de cohérence MV).
        static readonly int P_OceanDisp512     = Shader.PropertyToID("_OceanDisp512");
        static readonly int P_OceanDisp256     = Shader.PropertyToID("_OceanDisp256");
        // Interrupteur de consommation de l'absorption (global, branche uniforme — pas de variant).
        static readonly int P_OceanAbsorptionEnabled = Shader.PropertyToID("_OceanAbsorptionEnabled");
        // Écume : carte world-locked bindée à la surface + métadonnées de cascade lues pour le dispatch.
        static readonly int P_OceanFoam         = Shader.PropertyToID("_OceanFoam");
        static readonly int P_OceanFoamExtent   = Shader.PropertyToID("_OceanFoamExtent");
        static readonly int P_OceanFoamEnabled  = Shader.PropertyToID("_OceanFoamEnabled");
        static readonly int P_OceanCascade0     = Shader.PropertyToID("_OceanCascade0");
        static readonly int P_OceanCascade1     = Shader.PropertyToID("_OceanCascade1");
        static readonly int P_OceanCascade2     = Shader.PropertyToID("_OceanCascade2");
        static readonly int P_OceanCascade3     = Shader.PropertyToID("_OceanCascade3");
        static readonly int P_OceanCascadeCount = Shader.PropertyToID("_OceanCascadeCount");

        // =====================================================================
        //  Cycle
        // =====================================================================
        public override void OnModuleEnable(OceanApplyContext ctx)
        {
            var rt = new OceanSurfaceRuntime();
            EnsureMaterial(ctx, rt);
            EnsureGameObject(ctx, rt);
            rt.mv.Setup();
            ResolveFoamComputeEditorOnly();
            ctx.SetRuntime(this, rt);
        }

        public override void OnModuleDisable(OceanApplyContext ctx)
        {
            var rt = ctx.GetRuntime(this) as OceanSurfaceRuntime;
            if (rt != null)
            {
                rt.mv.Dispose();
                rt.foam.Dispose();
                if (rt.go != null) DestroyObj(rt.go);
                if (rt.mesh != null) DestroyObj(rt.mesh);
                if (rt.ownsMaterial && rt.material != null) DestroyObj(rt.material);
            }
            ctx.SetRuntime(this, null);
            // La restauration des globaux (_OceanDispPrev*, _OceanMVValid) est assurée par
            // OceanSystem.Teardown -> RestoreAll() (anti-bug n°1).
        }

        // PRÉ-SIMULATION : snapshot D[N-1] → prev AVANT l'évolution du spectre (cf. OceanSystem.PreSimulateAll).
        public override void PreSimulate(OceanApplyContext ctx)
        {
            var rt = ctx.GetRuntime(this) as OceanSurfaceRuntime;
            rt?.mv.SnapshotPrevious();
        }

        public override void Apply(OceanApplyContext ctx)
        {
            var rt = ctx.GetRuntime(this) as OceanSurfaceRuntime;
            if (rt == null) { OnModuleEnable(ctx); rt = ctx.GetRuntime(this) as OceanSurfaceRuntime; }
            if (rt == null || rt.material == null) return;

            EnsureGameObject(ctx, rt);           // recrée le GO si perdu (domain reload)
            RebuildMeshIfNeeded(rt);

            // Déplacement horizontal max AUTO-DÉRIVÉ des cascades (choppiness × hauteur), jamais saisi.
            float maxHoriz = DeriveMaxHorizontalDisplacement(ctx);
            float boundedY = maxWaveHeight.Effective * boundsSafetyScale.Effective;
            float boundedXZ = maxHoriz; // déjà multiplié par la marge dans DeriveMaxHorizontalDisplacement
            UpdateBoundsIfNeeded(rt, boundedY, boundedXZ);

            PushMaterialProps(rt, Mathf.Max(boundedY, boundedXZ));

            BindAbsorption(ctx);

            BindFoam(ctx, rt);

            BindMotionVectors(ctx, rt);
        }

        // ÉCUME : met à jour la carte world-locked APRÈS l'évolution du spectre de la frame.
        // Le spectre publie/évolue dans son Tick ; l'ordre du profil (Spectrum avant Surface) donne le
        // J de la frame courante. Si le profil était réordonné, la carte lirait la frame N-1 — bénin
        // (1 frame de latence), aucune garde supplémentaire requise.
        public override void Tick(OceanApplyContext ctx)
        {
            if (!foamEnabled.Effective || foamCompute == null) return;
            var rt = ctx.GetRuntime(this) as OceanSurfaceRuntime;
            if (rt == null) return;

            var cur512 = Shader.GetGlobalTexture(P_OceanDisp512);
            var cur256 = Shader.GetGlobalTexture(P_OceanDisp256);
            if (cur512 == null && cur256 == null) return;   // spectre absent/inactif → rien à accumuler

            // dt de décroissance : calculé DANS la feature (horloge réelle — Time.deltaTime vaut ~0
            // hors Play, ce qui fossilisait la traînée en mode édition).
            rt.foam.Dispatch(foamCompute, Mathf.Clamp(Mathf.ClosestPowerOfTwo(foamResolution), 256, 2048),
                cur512, cur256,
                Shader.GetGlobalVector(P_OceanCascade0), Shader.GetGlobalVector(P_OceanCascade1),
                Shader.GetGlobalVector(P_OceanCascade2), Shader.GetGlobalVector(P_OceanCascade3),
                Shader.GetGlobalFloat(P_OceanCascadeCount),
                gridExtent, jacobianThreshold.Effective, kFoamSoftness, foamFadeRate.Effective);
        }

        // ── Consommation ABSORPTION ──────────────────────────────────────────
        // La surface CONSOMME la source de vérité σ (_WaterAbsorption + _OceanAbsorptionDepth, poussées
        // par le SEUL OceanAbsorptionModule). Ici on ne pousse que l'INTERRUPTEUR de consommation
        // (branche uniforme côté shader, zéro variant/keyword) : 1 si le module absorption est
        // présent + actif + ancré, sinon 0 → le shader retombe sur _BaseColor (couleur de base). Couvre
        // aussi le toggle runtime de `active` (un module désactivé n'Apply plus : son σ resterait
        // périmé — l'interrupteur le rend inerte). Lecture d'état PUBLIC d'un autre module = pattern
        // déjà admis (cf. DeriveMaxHorizontalDisplacement / DisplacementParamHash sur le spectre).
        void BindAbsorption(OceanApplyContext ctx)
        {
            var abs = ctx.profile != null ? ctx.profile.Get<OceanAbsorptionModule>() : null;
            bool on = abs != null && abs.active && abs.HasAnchors;
            ctx.globals.SetGlobalFloat(P_OceanAbsorptionEnabled, on ? 1f : 0f);
        }

        // ── Consommation ÉCUME ───────────────────────────────────────────────
        // Même pattern que BindAbsorption : interrupteur + binds via ctx.globals (anti-bug n°1,
        // trackés/restaurés). Le bind nom→texture est fait ici ; le CONTENU des moments est mis à
        // jour par le dispatch du Tick (aucun re-bind requis — pattern MV). Un groupe absent est
        // rebindé sur le noir compatible array (jamais une RT détruite ; jamais échantillonné car
        // aucune cascade ne pointe ce groupe).
        // Binde la CARTE d'écume (résultat de la frame précédente) comme décal + son étendue monde +
        // l'interrupteur. La 1ʳᵉ frame (carte pas encore allouée) → noir + enabled=0 (aucun flash).
        void BindFoam(OceanApplyContext ctx, OceanSurfaceRuntime rt)
        {
            bool on = foamEnabled.Effective && foamCompute != null && rt.foam.Current != null;
            ctx.globals.SetGlobalTexture(P_OceanFoam, on ? (Texture)rt.foam.Current : Texture2D.blackTexture);
            ctx.globals.SetGlobalFloat(P_OceanFoamExtent, gridExtent);
            ctx.globals.SetGlobalFloat(P_OceanFoamEnabled, on ? 1f : 0f);
        }

        void ResolveFoamComputeEditorOnly()
        {
#if UNITY_EDITOR
            if (foamCompute == null)
                foamCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                    "Assets/Shader/Ocean_v2/Shaders/OceanFoam.compute");
#endif
        }

        // =====================================================================
        //  Binding Motion Vectors T-1 + garde de cohérence (anti-smear TAA)
        // =====================================================================
        // BINDING des globaux T-1 via ctx.globals UNIQUEMENT (anti-bug n°1 : tracké + restauré).
        // C'est le binding nom→texture qui est tracké ; la CopyTexture (PreSimulate) ne re-binde rien.
        //
        // INVARIANT D'ORDRE RÉEL (pas de fausse promesse « indépendant de l'ordre des modules ») :
        // le spectre publie _OceanDisp* dans son Tick, exécuté par OceanSystem APRÈS tous les Apply.
        // À l'instant du bind ci-dessous, _OceanDisp* reflète donc les arrays de la frame N-1 — cohérents
        // avec le snapshot prev pris en PreSimulate. Les arrays du spectre ne changent de DIMENSIONS qu'au
        // re-Setup complet (Teardown+Setup : changement de preset/résolution, domain reload), qui recrée
        // ce runtime surface (prev=null) → la (ré)allocation miroir (EnsureMirror) force alors _OceanMVValid=0.
        // La garde dimensionnelle ci-dessous est une DÉFENSE EN PROFONDEUR : elle ferme tout frame résiduel
        // où current et prev diffèrent en structure, sans dépendre du lieu exact de réallocation du spectre.
        void BindMotionVectors(OceanApplyContext ctx, OceanSurfaceRuntime rt)
        {
            var cur512 = Shader.GetGlobalTexture(P_OceanDisp512);
            var cur256 = Shader.GetGlobalTexture(P_OceanDisp256);

            // Rebind : prev réel s'il existe, sinon un NOIR COMPATIBLE ARRAY (jamais une RT détruite,
            // jamais un Texture2D simple sur un sampler-array). Cas typique : preset Low sans groupe 512².
            ctx.globals.SetGlobalTexture(OceanMotionVectorPass.ID_DispPrev512,
                rt.mv.Prev512 != null ? (Texture)rt.mv.Prev512 : rt.mv.BlackArray);
            ctx.globals.SetGlobalTexture(OceanMotionVectorPass.ID_DispPrev256,
                rt.mv.Prev256 != null ? (Texture)rt.mv.Prev256 : rt.mv.BlackArray);

            // _OceanMVValid = 0 (⇒ le domain échantillonne prev=current ⇒ MV nuls ce frame) si l'une des
            // conditions de discontinuité est vraie :
            //   (a) le tampon prev vient d'être (ré)alloué ce frame (EnsureMirror → ValidThisFrame=false) ;
            //   (b) current et prev diffèrent en structure (fenêtre d'un frame au switch de preset) ;
            //   (c) saut réel du champ de vagues (slider LookDev : état de mer / amplitude / choppiness…),
            //       à dimensions inchangées → le déplacement bouge d'un coup, MV énormes non désirés.
            bool dimsOK = SameArrayDims(cur512, rt.mv.Prev512) && SameArrayDims(cur256, rt.mv.Prev256);

            int dispHash = DisplacementParamHash(ctx);
            bool paramJump = dispHash != rt.dispParamHash;
            rt.dispParamHash = dispHash;

            bool mvValid = rt.mv.ValidThisFrame && dimsOK && !paramJump;
            ctx.globals.SetGlobalFloat(OceanMotionVectorPass.ID_MVValid, mvValid ? 1f : 0f);
        }

        // Cohérence structurelle entre l'array de déplacement COURANT (global du spectre) et le tampon prev.
        //  - les deux nuls  → groupe de cascade absent des deux côtés (jamais échantillonné) → cohérent ;
        //  - un seul nul    → apparition/disparition d'un groupe (ex. Low↔High) → INCOHÉRENT (MV nuls) ;
        //  - dimensions/format/dimension d'array différents → INCOHÉRENT (MV nuls).
        // internal (et non private) UNIQUEMENT pour le smoke test EditMode (InternalsVisibleTo →
        // Ombrage.OceanFeatures.Tests) : la logique de cohérence dimensionnelle du durcissement 3C est
        // couverte hors éditeur. Aucune autre modification de visibilité.
        internal static bool SameArrayDims(Texture current, RenderTexture prev)
        {
            bool curNull = current == null;
            bool prevNull = prev == null;
            if (curNull && prevNull) return true;
            if (curNull != prevNull) return false;
            var cr = current as RenderTexture;
            if (cr == null) return false;   // type inattendu → prudence (MV nuls)
            return cr.width == prev.width
                && cr.height == prev.height
                && cr.volumeDepth == prev.volumeDepth
                && cr.graphicsFormat == prev.graphicsFormat
                && cr.dimension == prev.dimension;
        }

        // Hash des paramètres du SPECTRE qui déterminent le CHAMP DE VAGUES (donc le déplacement). Un
        // changement ⇒ discontinuité de position ⇒ on invalide les MV ce frame (anti-smear TAA en LookDev).
        // Lit UNIQUEMENT des champs PUBLICS sérialisés du module spectre (aucun accès à son runtime privé :
        // le spectre reste byte-à-byte intact). Ce couplage lecture-seule existe déjà (cf. DeriveMaxHorizontalDisplacement).
        int DisplacementParamHash(OceanApplyContext ctx)
        {
            var s = ctx.profile != null ? ctx.profile.Get<OceanSpectrumModule>() : null;
            if (s == null) return 0;
            unchecked
            {
                int h = 17;
                h = h * 31 + (int)s.cascadeQuality;
                h = h * 31 + s.masterTileLength.Effective.GetHashCode();
                h = h * 31 + s.bandBoundary.Effective.GetHashCode();
                h = h * 31 + s.oceanState.Effective.GetHashCode();
                h = h * 31 + s.windSpeedAtMax.Effective.GetHashCode();
                h = h * 31 + s.windDirectionDeg.Effective.GetHashCode();
                h = h * 31 + s.fetch.Effective.GetHashCode();
                h = h * 31 + s.gamma.Effective.GetHashCode();
                h = h * 31 + s.amplitude.Effective.GetHashCode();
                h = h * 31 + s.choppiness.Effective.GetHashCode();
                h = h * 31 + s.depth.Effective.GetHashCode();
                h = h * 31 + (s.useTMA.Effective ? 1 : 0);
                return h;
            }
        }

        // =====================================================================
        //  Matériau / GameObject / mesh
        // =====================================================================
        void EnsureMaterial(OceanApplyContext ctx, OceanSurfaceRuntime rt)
        {
            if (surfaceMaterialOverride != null) { rt.material = surfaceMaterialOverride; rt.ownsMaterial = false; return; }
            if (ctx.material != null) { rt.material = ctx.material; rt.ownsMaterial = false; return; }

            var sh = Shader.Find(kShaderName);
#if UNITY_EDITOR
            if (sh == null) sh = AssetDatabase.LoadAssetAtPath<Shader>(kShaderPath);
#endif
            if (sh == null)
            {
                Debug.LogWarning("[Ocean] Shader '" + kShaderName + "' introuvable — surface inactive.");
                return;
            }
            rt.material = new Material(sh) { name = "OceanSurface (auto)", hideFlags = HideFlags.DontSave };
            rt.ownsMaterial = true;
        }

        void EnsureGameObject(OceanApplyContext ctx, OceanSurfaceRuntime rt)
        {
            if (rt.material == null) return;
            if (rt.go != null)
            {
                if (rt.renderer != null && rt.renderer.sharedMaterial != rt.material)
                    rt.renderer.sharedMaterial = rt.material;
                return;
            }

            rt.go = new GameObject("OceanSurface (runtime)")
            {
                // Non sérialisé, non sélectionnable, détruit avec le système (jamais en scène à la sauvegarde).
                hideFlags = HideFlags.HideAndDontSave
            };
            if (ctx.system != null) rt.go.transform.SetParent(ctx.system.transform, worldPositionStays: false);

            rt.filter = rt.go.AddComponent<MeshFilter>();
            rt.renderer = rt.go.AddComponent<MeshRenderer>();
            rt.renderer.sharedMaterial = rt.material;
            // Per Object Motion : indispensable pour que HDRP déclenche la passe MotionVectors native.
            rt.renderer.motionVectorGenerationMode = MotionVectorGenerationMode.Object;
            // Pas d'ombre castée par la surface : une surface OPAQUE qui ombre le volume qu'elle
            // borde black-out TOUT le dessous → le soleil n'éclaire ni le fog volumétrique (pas de glow) ni
            // les objets immergés (pas d'ombres sous-marines). En pleine mer, l'auto-ombrage émergé des
            // vagues est négligeable → on coupe le shadow-casting. (Réactivable/gaté émersion si besoin.)
            rt.renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rt.renderer.allowOcclusionWhenDynamic = false;

            rt.gridParamHash = int.MinValue;   // force la (re)construction du mesh
            RebuildMeshIfNeeded(rt);
        }

        void RebuildMeshIfNeeded(OceanSurfaceRuntime rt)
        {
            int h = unchecked(baseResolution * 397 ^ gridExtent.GetHashCode());
            if (rt.mesh != null && h == rt.gridParamHash) return;

            if (rt.mesh != null) DestroyObj(rt.mesh);
            rt.mesh = OceanSurfaceRuntime.GenerateUniformGrid(baseResolution, gridExtent);
            rt.gridParamHash = h;
            rt.boundsParamHash = int.MinValue;   // bounds à recalculer pour le nouveau mesh
            if (rt.filter != null) rt.filter.sharedMesh = rt.mesh;
        }

        void UpdateBoundsIfNeeded(OceanSurfaceRuntime rt, float boundedY, float boundedXZ)
        {
            int h = unchecked(boundedY.GetHashCode() * 31 ^ boundedXZ.GetHashCode() * 17 ^ gridExtent.GetHashCode());
            if (h == rt.boundsParamHash) return;
            rt.SetBounds(boundedY, boundedXZ, gridExtent);
            rt.boundsParamHash = h;
        }

        void PushMaterialProps(OceanSurfaceRuntime rt, float maxDispEnvelope)
        {
            var m = rt.material;
            m.SetColor(P_BaseColor, baseColor.Effective);
            m.SetFloat(P_Smoothness, smoothness.Effective);
            m.SetFloat(P_Metallic, metallic.Effective);

            // On désactive le gating distance NATIF de HDRP (notre GetTessellationFactor calcule le
            // facteur quantifié + caméra de référence snappée côté shader, pour la stabilité MV).
            m.SetFloat(P_TessFactor, maxTessFactor.Effective);
            m.SetFloat(P_TessMinDistHDRP, 0f);
            m.SetFloat(P_TessMaxDistHDRP, 0f);
            m.SetFloat(P_TessTriSize, 0f);
            m.SetFloat(P_TessShape, 0f);                 // pas de Phong tessellation (_TESSELLATION_PHONG off)
            m.SetFloat(P_TessBackCull, -1f);             // back-face cull tess OFF (vagues déplacées ; single-sided géré par Cull Back)

            m.SetFloat(P_OceanTessMin, tessMinDist.Effective);
            m.SetFloat(P_OceanTessMax, Mathf.Max(tessMaxDist.Effective, tessMinDist.Effective + 0.01f));
            m.SetFloat(P_OceanTessLevels, tessQuantLevels.Effective);
            m.SetFloat(P_OceanRefCamSnap, refCamSnap.Effective);
            m.SetFloat(P_OceanMaxDisp, maxDispEnvelope);
        }

        // Borne conservative du déplacement horizontal (XZ) à partir des paramètres de cascades du spectre.
        // Relation physique : le déplacement de choppiness (Gerstner/FFT horizontal) est proportionnel à
        // la pente, donc à choppiness × amplitude verticale. On majore par boundsSafetyScale. Coût CPU négligeable.
        float DeriveMaxHorizontalDisplacement(OceanApplyContext ctx)
        {
            float choppiness = 1f;
            var spectrum = ctx.profile != null ? ctx.profile.Get<OceanSpectrumModule>() : null;
            if (spectrum != null) choppiness = Mathf.Max(0f, spectrum.choppiness.Effective);
            // Horizontal ∝ choppiness × hauteur de vague ; marge de sécurité incluse.
            return choppiness * maxWaveHeight.Effective * boundsSafetyScale.Effective;
        }

        static void DestroyObj(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Object.Destroy(o);
            else Object.DestroyImmediate(o);
        }

        // =====================================================================
        //  Validation (anti-NaN géométrie / clamps hardware)
        // =====================================================================
#if UNITY_EDITOR
        void OnValidate()
        {
            // Contrainte d'ORDRE la plus dangereuse : dénominateur (tessMax - tessMin) > 0 dans le shader.
            tessMinDist.value = Mathf.Max(0f, tessMinDist.value);
            if (tessMaxDist.value <= tessMinDist.value) tessMaxDist.value = tessMinDist.value + 1f;
            maxTessFactor.value = Mathf.Clamp(maxTessFactor.value, 1f, 64f);   // limite hardware
            tessQuantLevels.value = Mathf.Clamp(tessQuantLevels.value, 2, 32);
            refCamSnap.value = Mathf.Max(0f, refCamSnap.value);
            gridExtent = Mathf.Max(1f, gridExtent);                            // structure → champ simple
            baseResolution = Mathf.Clamp(baseResolution, 2, 254);              // structure → champ simple
            maxWaveHeight.value = Mathf.Max(0.01f, maxWaveHeight.value);
            boundsSafetyScale.value = Mathf.Clamp(boundsSafetyScale.value, 1f, 3f);
            jacobianThreshold.value = Mathf.Clamp(jacobianThreshold.value, 0.5f, 1.05f);
            foamFadeRate.value = Mathf.Max(0f, foamFadeRate.value);
            foamResolution = Mathf.Clamp(Mathf.ClosestPowerOfTwo(foamResolution), 256, 2048);  // structure
            ResolveFoamComputeEditorOnly();
        }
#endif
    }
}
