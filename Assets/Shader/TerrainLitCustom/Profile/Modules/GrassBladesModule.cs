// GrassBladesModule.cs
// Feature Grass Blades L0 (brins géométriques) en override du TerrainProfile, jumeau de
// GrassTintModule (L2). Le TUNING + les refs d'ASSETS (material/compute/diffusion profile/mesh)
// vivent ici (SO, UI Volume) ; l'état runtime GPU (BRG + buffers + compute) vit dans un
// GrassBladesRuntime détenu par le contrôleur (un SO ne porte ni ressources GPU ni état par instance).
//
// Placement : 100% piloté par les ESPÈCES (section Multi-espèce). Chaque espèce a une densité par
// couche Terrain (0-7) ; sur une couche les densités se partagent un budget de 1. (Remplace l'ancien
// champ unique "Layers".)
// Vent : AUCUNE config ici — les brins lisent les globals _GrassWind* PARTAGÉS poussés par
// GrassTintModule (source unique). Si GrassTint est absent du profil, pas de vent.
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace Ombrage.TerrainFeatures
{
    // Per-layer density (0..1) for a species: how much of each Terrain layer (0-7) this species fills.
    // On a given layer the species' densities SHARE a budget of 1 (A=0.75 leaves 0.25 for the others);
    // the runtime normalizes the share and the keep probability = the layer's total density.
    [System.Serializable]
    public class LayerDensity
    {
        [SerializeField] public bool[]  enabled = new bool[8];  // does the species grow on layer i (toggle)
        [SerializeField] public float[] value   = new float[8]; // density on layer i (slider, kept even at 0)
        // Effective density: 0 unless the layer is enabled. A slider at 0 with the layer still enabled
        // contributes 0 but stays toggled on.
        public float this[int i] =>
            (enabled != null && value != null && i >= 0 && i < 8 && enabled[i]) ? value[i] : 0f;
    }

    // One vegetation species: shape + color + per-layer density (where & how dense it grows). Empty
    // species list -> one implicit default species (the "Forme de lame"/"Couleur" fields) on layer 0.
    [System.Serializable]
    public class SpeciesEntry
    {
        public string name = "Grass";
        [Tooltip("Mesh custom (fleur, touffe…). VIDE = brin Bézier procédural (height/width/bend/tilt). Avec un mesh, height = échelle, width ignorée.")]
        public Mesh mesh;
        public Color color = new Color(0.30f, 0.45f, 0.10f);
        [Range(0f, 1f)] public float colorVariation = 0.5f;
        [Min(0.02f)] public float height = 0.5f;
        [Range(0f, 1f)] public float heightRandom = 0.3f;
        [Min(0.005f)] public float width = 0.05f;
        [Range(0f, 1f)] public float bend = 0.35f;
        [Range(0f, 0.5f)] public float tilt = 0.15f;
        [Tooltip("Densité par couche Terrain (0-7). 0 = absente. Sur une couche, les densités des espèces se partagent un budget de 1.")]
        public LayerDensity layerDensity = new LayerDensity();
    }

    [TerrainModuleMenu("Vegetation/Grass Blades")]
    public class GrassBladesModule : TerrainFeatureModule
    {
        [Header("Assets")]
        [Tooltip("Matériau des brins d'herbe.")]
        public Material material;
        [Tooltip("Shader de calcul (.compute) qui répartit les brins sur le terrain.")]
        public ComputeShader scatterCompute;
        [Tooltip("Forme de brin personnalisée (optionnel). Vide = brin généré automatiquement.")]
        public Mesh meshOverride;
        [Tooltip("Profil de diffusion HDRP pour la translucence (lumière qui traverse les brins). Doit être enregistré dans la liste des Project Settings HDRP.")]
        public DiffusionProfileSettings diffusionProfile;

        [Header("Placement / densité")]
        [Tooltip("Les brins suivent la caméra (apparaissent autour d'elle). Décoché : grille fixe centrée sur l'objet (mode simple, sans suivi).")]
        public bool cameraCentered = true;
        [Tooltip("Densité visée à l'écran : taille en pixels d'une cellule de brins vue de près. Plus petit = herbe plus dense et nette, mais plus coûteux.")]
        [Range(3f, 32f)] public float cellPixels = 8f;
        [Tooltip("Espacement entre brins proches (m). Plus petit = plus dense.")]
        [Min(0.01f)] public float spacing = 0.15f;
        [Tooltip("Adoucit la limite entre les zones de densité quand on s'éloigne. 0 = bord net, 0.5 = transition très douce.")]
        [Range(0f, 0.5f)] public float lodTransition = 0.3f;
        [Tooltip("Calcul sur GPU (recommandé). Décoché : repli sur CPU (grille plate, plus simple, sans gestion de la distance).")]
        public bool useCompute = true;

        [Header("Forme des brins")]
        [Tooltip("Désordre de position des brins dans leur cellule. 0 = alignés en grille, 1 = bien dispersés.")]
        [Range(0f, 1f)] public float positionJitter = 0.6f;
        [Tooltip("Nombre de segments d'un brin (sa finesse). Plus haut = brin plus courbe et lisse de près.")]
        [Range(1, 8)] public int bladeSegments = 5;

        [Header("Vent")]
        [Tooltip("Amplitude du pliage des brins sous le vent. 0 = brins rigides. La force, la direction et les rafales du vent se règlent dans l'override Grass Tint (vent partagé entre l'herbe et le sol).")]
        [Range(0f, 1.5f)] public float windBendGain = 0.4f;

        [Header("Déformation (écrasement au sol)")]
        [Tooltip("Force avec laquelle les brins se couchent là où le sol est écrasé (véhicules, joueur). 0 = désactivé. Nécessite l'override Deformation sur le profil. Les brins s'évasent autour du point écrasé.")]
        [Range(0f, 8f)] public float deformInfluence = 3f;

        [Header("Espèces")]
        [Tooltip("Liste des espèces d'herbe (forme, couleur, densité par couche de terrain). VIDE = aucune herbe (système désactivé). 1 espèce = une seule ; plusieurs = mélange réparti par couche.")]
        public SpeciesEntry[] species;

        [Header("Surface (apparence)")]
        [Tooltip("Redresse l'éclairage des brins vers le haut. 0 = éclairé de face, 1 = comme une surface plate face au ciel. Monte si les brins paraissent trop sombres.")]
        [Range(0f, 1f)] public float normalUp = 0.5f;
        [Tooltip("Arrondit l'éclairage sur la largeur du brin (effet de volume, pas plat). 0 = plat, 1 = bien arrondi.")]
        [Range(0f, 1f)] public float normalRound = 0.4f;
        [Tooltip("Assombrissement à la base des brins (ombrage d'ambiance). 0 = uniforme, 1 = base bien sombre.")]
        [Range(0f, 1f)] public float bladeAO = 0.4f;
        [Tooltip("Hauteur sur laquelle l'assombrissement de base s'estompe (fraction du brin, de la base vers la pointe).")]
        [Range(0.05f, 1f)] public float bladeAOHeight = 0.3f;
        [Tooltip("Réduit le scintillement des reflets sur les brins lointains en les rendant plus mats avec la distance. 0 = désactivé.")]
        [Range(0f, 1f)] public float specularAA = 0.7f;
        [Tooltip("Distance (% de la portée max) à laquelle l'anti-scintillement des reflets est au maximum.")]
        [Range(0f, 100f)] public float specularAADistancePct = 33f;

        [Header("Translucence (lumière à travers les brins)")]
        [Tooltip("Épaisseur apparente des brins pour la lumière qui les traverse. Plus fin = plus de lumière passe (effet rétro-éclairé à contre-jour).")]
        [Range(0f, 1f)] public float thickness = 0.1f;
        [Tooltip("Force de la lumière qui traverse les brins à contre-jour (effet lumineux).")]
        [Range(0f, 2f)] public float transmission = 1f;

        [Header("Densité globale")]
        [Tooltip("Éclaircit toutes les espèces d'un coup. La densité par couche se règle par espèce dans 'Espèces'.")]
        [Range(0f, 1f)] public float density = 1f;

        [Header("Touffes")]
        [Tooltip("Regroupe les brins en touffes (plus naturel qu'une répartition uniforme).")]
        public bool useClumping = true;
        [Tooltip("Taille des touffes (m).")]
        [Range(0.3f, 10f)] public float clumpSize = 2f;
        [Tooltip("Variation de hauteur d'une touffe à l'autre. 0 = toutes pareilles.")]
        [Range(0f, 1f)] public float clumpHeightVariation = 0.5f;
        [Tooltip("Force avec laquelle les brins se rapprochent du centre de leur touffe.")]
        [Range(0f, 1f)] public float clumpPullStrength = 0.4f;
        [Tooltip("Alignement des brins d'une même touffe dans la même direction.")]
        [Range(0f, 1f)] public float clumpDirectionStrength = 0.5f;
        [Tooltip("Variation de couleur d'une touffe à l'autre.")]
        [Range(0f, 1f)] public float clumpColorVariation = 0.5f;

        [Header("Distance & niveau de détail")]
        [Tooltip("Marge autour du champ de vision (m) : garde un peu d'herbe juste hors écran pour éviter qu'elle apparaisse d'un coup sur les bords quand tu tournes la caméra.")]
        [Min(0f)] public float frustumMargin = 2f;
        [Tooltip("Tolérance (m) avant de masquer l'herbe cachée derrière un objet. Environ la hauteur d'un brin. Plus grand = plus prudent (masque moins, évite les erreurs de masquage).")]
        [Range(0.05f, 5f)] public float occlusionBias = 1f;
        [Tooltip("PORTÉE MAX des brins (m), que tu règles toi-même. Toutes les autres distances ci-dessous en dérivent en %. Au-delà, le sol prend la couleur d'herbe (override Grass Tint).")]
        [Min(5f)] public float maxBladeDistance = 120f;
        [Tooltip("Largeur de la zone (% de la portée max) où, tout au bout, les brins disparaissent en douceur avant la portée max. 0 = coupure nette. N'affecte QUE les brins ; la teinte du sol se règle dans l'override Grass Tint.")]
        [Range(0f, 50f)] public float crossfadeBandPct = 25f;
        [Tooltip("Fait disparaître les brins lointains en se dissolvant pixel par pixel, en plus de rapetisser. 0 = rapetissement seul. Monte pour une disparition plus douce.")]
        [Range(0f, 1f)] public float ditherStrength = 0f;
        [Tooltip("Force de l'élargissement des brins lointains (pour qu'ils restent ~1 pixel au lieu de scintiller). 0 = désactivé. La portée et la distribution se règlent juste en dessous.")]
        [Range(0f, 0.02f)] public float lodWidthClamp = 0.004f;
        [Tooltip("Portée d'application de l'élargissement (% de la portée max), depuis Max Blade Distance vers la caméra. Ex : 70 % avec Max 300 → l'élargissement va de 90 m (0 %) à 300 m (100 %).")]
        [Range(0f, 100f)] public float widthClampReachPct = 70f;
        [Tooltip("Distribution de l'élargissement sur sa portée. X = 0 (côté caméra) → 1 (Max Blade Distance) ; Y = quantité d'élargissement (0 = largeur naturelle, 1 = élargissement plein).")]
        public AnimationCurve widthClampCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        [Tooltip("Distance (% de la portée max) à partir de laquelle les brins commencent à se simplifier (moins de détail) en s'éloignant. 100 % = jamais simplifiés (détail plein partout).")]
        [Range(0f, 100f)] public float bandSplitPct = 25f;
        [Tooltip("Finesse des brins les plus lointains une fois complètement simplifiés. Plus bas = brins lointains plus simples. Sans effet sur les espèces à forme personnalisée (fleurs, touffes).")]
        [Range(1, 6)] public int lowMeshSegments = 2;

        [Header("Cartes d'herbe lointaine (impostors)")]
        [Tooltip("Nombre de brins dans la touffe servant à fabriquer la carte d'herbe lointaine.")]
        [Range(4, 64)] public int impostorBladeCount = 28;
        [Tooltip("Rayon de la touffe (m) — étale les brins au sol.")]
        [Min(0.05f)] public float impostorTuftRadius = 0.35f;
        [Tooltip("Hauteur de la touffe (m).")]
        [Min(0.1f)] public float impostorTuftHeight = 0.6f;
        [Tooltip("Résolution verticale de la texture générée (la largeur suit les proportions).")]
        [Range(64, 1024)] public int impostorResolution = 256;
        [Tooltip("Espacement des cartes au sol (m). Plus petit = champ plus dense (plus de cartes).")]
        [Range(2f, 16f)] public float impostorSpacing = 5f;
        [Tooltip("Portée d'affichage des cartes = ce facteur × Max Blade Distance. Plus grand = l'herbe va plus loin.")]
        [Range(1f, 5f)] public float impostorReach = 2.5f;
        [Tooltip("Taille des cartes (× la touffe bakée). Plus grand = cartes plus grosses.")]
        [Range(0.5f, 6f)] public float impostorCardScale = 2f;
        [Tooltip("Coche pour générer la touffe et sauver l'image sur le Bureau (GrassImpostor.png). Se décoche tout seul.")]
        public bool previewImpostorBake;

        [Header("Debug")]
        [Tooltip("L'herbe se calcule toujours depuis la caméra principale (Game), même quand tu regardes par la fenêtre Scene. Pratique pour inspecter le résultat de l'extérieur. Décoche pour que l'aperçu suive la caméra de la Scene pendant que tu édites.")]
        public bool cullFromMainCamera = true;

        [Header("Ombres")]
        [Tooltip("Nombre de cascades d'ombre qui reçoivent les ombres d'herbe (0 = pas d'ombres d'herbe).")]
        [Range(0, 4)] public int shadowCascades = 2;
        [Tooltip("Distance max des ombres d'herbe (% de la portée max), autour de la caméra.")]
        [Range(0f, 100f)] public float shadowDistancePct = 50f;
        [Tooltip("Ne projette les ombres que pour l'herbe visible à l'écran (+ une marge), au lieu de tout autour à 360°. Évite les ombres sans herbe visible et coûte moins cher. Décoche pour des ombres tout autour de la caméra.")]
        public bool shadowFrustumCull = true;
        [Tooltip("Largeur (% de la distance d'ombre) sur laquelle les ombres s'estompent au bord, pour éviter un cercle d'ombre net qui balaie le sol quand tu montes ou descends. 0 = bord net.")]
        [Range(0f, 100f)] public float shadowFadePct = 25f;
        [Tooltip("Quantité d'herbe qui projette des ombres. Plus bas = ombres moins denses et moins coûteuses.")]
        [Range(0f, 1f)] public float shadowDensity = 0.7f;
        [Tooltip("Fausse ombre au-delà de la distance d'ombre : assombrit l'herbe (brins + sol teinté) pour simuler l'ombrage de la prairie là où les vraies ombres ne portent plus. 0 = désactivé.")]
        [Range(0f, 1f)] public float fakeShadowStrength = 0.5f;

        // Set by OnValidate; the runtime rebuilds (mesh + buffers) on the next Apply when set.
        [System.NonSerialized] public bool dirty;

        // --- Distances dérivées (mètres) du master maxBladeDistance --------------------------------
        // Les curseurs de distance ci-dessus sont des POURCENTAGES (toujours actifs) ; le système pousse
        // CES valeurs en mètres au shader/compute. Régler maxBladeDistance rescale tout, les % restent.
        public float CrossfadeBandM      => maxBladeDistance * crossfadeBandPct      * 0.01f;
        public float BandSplitM          => maxBladeDistance * bandSplitPct          * 0.01f;
        public float ShadowDistanceM     => maxBladeDistance * shadowDistancePct     * 0.01f;
        public float ShadowFadeBandM     => ShadowDistanceM  * shadowFadePct         * 0.01f;
        public float SpecularAADistanceM => maxBladeDistance * specularAADistancePct * 0.01f;

        // Last (material, profile) pair pushed via HDMaterial.SetDiffusionProfile — it writes
        // material properties (dirties the material), so only re-apply on change, not every frame.
        [System.NonSerialized] Material m_ProfileAppliedTo;
        [System.NonSerialized] DiffusionProfileSettings m_AppliedProfile;
        // Baked width-clamp distribution curve (256x1 ramp) + content hash (re-bake only on change).
        [System.NonSerialized] Texture2D m_WidthCurveTex;
        [System.NonSerialized] int m_WidthCurveHash;

        // Grass has its own BRG material — no terrain-material keyword.
        public override string Keyword => null;

        static readonly int ID_NormalUp      = Shader.PropertyToID("_GrassNormalUp");
        static readonly int ID_NormalRound   = Shader.PropertyToID("_GrassNormalRound");
        static readonly int ID_AO            = Shader.PropertyToID("_GrassAO");
        static readonly int ID_AOHeight      = Shader.PropertyToID("_GrassAOHeight");
        static readonly int ID_Thickness     = Shader.PropertyToID("_GrassThickness");
        static readonly int ID_Transmission  = Shader.PropertyToID("_GrassTransmission");
        static readonly int ID_LODWidthClamp = Shader.PropertyToID("_GrassLODWidthClamp");
        static readonly int ID_WidthCurve      = Shader.PropertyToID("_GrassWidthCurve");
        static readonly int ID_WidthClampStart = Shader.PropertyToID("_GrassWidthClampStart");
        static readonly int ID_WidthClampEnd   = Shader.PropertyToID("_GrassWidthClampEnd");
        static readonly int ID_WindBendGain  = Shader.PropertyToID("_GrassWindBendGain");
        static readonly int ID_DeformInfluence = Shader.PropertyToID("_GrassDeformInfluence");
        static readonly int ID_CrossfadeStart = Shader.PropertyToID("_GrassCrossfadeStart");
        static readonly int ID_CrossfadeEnd   = Shader.PropertyToID("_GrassCrossfadeEnd");
        static readonly int ID_ShadowDist     = Shader.PropertyToID("_GrassShadowDist");
        static readonly int ID_ShadowFadeBand = Shader.PropertyToID("_GrassShadowFadeBand");
        static readonly int ID_FakeShadow     = Shader.PropertyToID("_GrassFakeShadowStrength");
        static readonly int ID_SpecularAA     = Shader.PropertyToID("_GrassSpecularAA");
        static readonly int ID_SpecularAADist = Shader.PropertyToID("_GrassSpecularAADist");
        static readonly int ID_DitherStrength = Shader.PropertyToID("_GrassDitherStrength");
        static readonly int ID_RingFeather    = Shader.PropertyToID("_GrassRingFeather");
        static readonly int ID_CollapseStart  = Shader.PropertyToID("_GrassCollapseStart");
        static readonly int ID_CollapseEnd    = Shader.PropertyToID("_GrassCollapseEnd");
        static readonly int ID_CollapseTargetSeg = Shader.PropertyToID("_GrassCollapseTargetSeg");

        void OnValidate() => dirty = true;

        public override void OnModuleEnable(TerrainApplyContext ctx)
        {
            // Safe default for the shared deformation global: black = no press. If a Deformation
            // override exists it overwrites this with its RT every frame; if not, the grass samples
            // black (no bend) instead of an unbound texture (which reads as white = uniform bend).
            Shader.SetGlobalTexture(Shader.PropertyToID("_DeformationMap"), Texture2D.blackTexture);

            var rt = new GrassBladesRuntime();
            rt.Initialize(this, ctx);
            ctx.SetRuntime(this, rt);
        }

        public override void OnModuleDisable(TerrainApplyContext ctx)
        {
            // Module off: stop the blade fade-out band and the fake far shadow.
            Shader.SetGlobalFloat(ID_CrossfadeEnd, 0f);
            Shader.SetGlobalFloat(ID_FakeShadow, 0f);
            TerrainRampBaker.Release(ref m_WidthCurveTex);
            (ctx.GetRuntime(this) as GrassBladesRuntime)?.Dispose();
        }

        public override void Apply(TerrainApplyContext ctx)
        {
            // Blade surface globals (read by the grass shader) — pushed every frame for live edit.
            Shader.SetGlobalFloat(ID_NormalUp, normalUp);
            Shader.SetGlobalFloat(ID_NormalRound, normalRound);
            Shader.SetGlobalFloat(ID_AO, bladeAO);
            Shader.SetGlobalFloat(ID_AOHeight, bladeAOHeight);
            Shader.SetGlobalFloat(ID_Thickness, thickness);
            Shader.SetGlobalFloat(ID_Transmission, transmission);
            Shader.SetGlobalFloat(ID_LODWidthClamp, lodWidthClamp);
            // Width-clamp distance ramp + distribution curve (baked). Ramps from maxBladeDistance toward
            // the camera over reach% of the range; the curve shapes the fade-in. Independent of collapse.
            TerrainRampBaker.Bake(widthClampCurve, ref m_WidthCurveTex, ref m_WidthCurveHash, "GrassWidthCurve");
            Shader.SetGlobalTexture(ID_WidthCurve, m_WidthCurveTex != null ? (Texture)m_WidthCurveTex : Texture2D.blackTexture);
            float wReach = Mathf.Clamp01(widthClampReachPct * 0.01f);
            Shader.SetGlobalFloat(ID_WidthClampStart, maxBladeDistance * (1f - wReach));
            Shader.SetGlobalFloat(ID_WidthClampEnd,   maxBladeDistance);
            Shader.SetGlobalFloat(ID_WindBendGain, windBendGain);
            Shader.SetGlobalFloat(ID_SpecularAA, specularAA);
            // Deformation influence (reads the shared _DeformationMap). Off when grass is off so an
            // unbound deformation RT is never sampled.
            Shader.SetGlobalFloat(ID_DeformInfluence, (active && species != null && species.Length > 0) ? deformInfluence : 0f);
            Shader.SetGlobalFloat(ID_SpecularAADist, SpecularAADistanceM);
            // The grass system is ON only when active AND at least one species is defined (no default
            // grass). The fake far shadow also darkens the terrain tint, so when grass is off it must be 0.
            bool grassOn = active && species != null && species.Length > 0;

            // Blade fade-out band: the blades melt to height 0 over [End - band, End] (End = max blade
            // distance). Read ONLY by the blade vertex — the terrain tint uses its OWN distance range
            // (Grass Tint module), independent of this.
            bool xfadeOn = grassOn && crossfadeBandPct > 0f;
            Shader.SetGlobalFloat(ID_CrossfadeStart, xfadeOn ? maxBladeDistance - CrossfadeBandM : 0f);
            Shader.SetGlobalFloat(ID_CrossfadeEnd,   xfadeOn ? maxBladeDistance : 0f);
            // Screen-door dither (1b): strength gated on grass being on. The dither pattern animates
            // via HDRP's own _TaaFrameInfo.z (read in the shader) so TAA resolves the dissolve.
            Shader.SetGlobalFloat(ID_DitherStrength, grassOn ? ditherStrength : 0f);
            // Clipmap ring overflow feather (= lodTransition) — drives the ring anti-pop dither (surface).
            Shader.SetGlobalFloat(ID_RingFeather, Mathf.Clamp01(lodTransition));
            // 1a vertex collapse: ramp from BandSplitM (full detail) to maxBladeDistance
            // (collapsed to lowMeshSegments rows). End<=Start disables it (incl. grass off).
            Shader.SetGlobalFloat(ID_CollapseStart, grassOn ? BandSplitM : 0f);
            Shader.SetGlobalFloat(ID_CollapseEnd,   grassOn ? maxBladeDistance : 0f);
            Shader.SetGlobalFloat(ID_CollapseTargetSeg, lowMeshSegments);
            // Fake far shadow (read by blades + L2 tint). Off when grass is off.
            Shader.SetGlobalFloat(ID_ShadowDist,     ShadowDistanceM);
            Shader.SetGlobalFloat(ID_ShadowFadeBand, ShadowFadeBandM);
            Shader.SetGlobalFloat(ID_FakeShadow,     grassOn ? fakeShadowStrength : 0f);
            if (material != null && diffusionProfile != null &&
                (material != m_ProfileAppliedTo || diffusionProfile != m_AppliedProfile))
            {
                HDMaterial.SetDiffusionProfile(material, diffusionProfile);
                m_ProfileAppliedTo = material;
                m_AppliedProfile   = diffusionProfile;
            }

            // Build/rebuild the BRG + buffers if needed (first frame, or after an inspector change).
            (ctx.GetRuntime(this) as GrassBladesRuntime)?.EnsureBuilt(this, ctx);
        }

        public override void Tick(TerrainApplyContext ctx)
        {
            (ctx.GetRuntime(this) as GrassBladesRuntime)?.Tick(this, ctx);
        }
    }
}
