// GrassTintModule.cs
// Feature Grass Tint L2 (keyword _GRASS_TINT) : le terrain se teinte vers la couleur d'herbe
// avec la distance caméra (trompe-l'œil aérien) + bandes de vent (normal/luminance).
// Per-layer : quelles couches lisent comme de l'herbe à distance.
// Porté depuis TerrainLitCustomSetup (bloc Grass Tint).
using UnityEngine;

namespace Ombrage.TerrainFeatures
{
    [TerrainModuleMenu("Surface/Grass Tint")]
    public class GrassTintModule : TerrainFeatureModule
    {
        [Tooltip("Couches du terrain (0-7) qui prennent la couleur d'herbe au loin (les mêmes que celles où poussent les brins).")]
        public LayerToggleParameter layers = new LayerToggleParameter(new bool[8], true);
        [Tooltip("Couleur vers laquelle le terrain se teinte au loin.")]
        public ColorParameter tintColor = new ColorParameter(new Color(0.25f, 0.4f, 0.15f, 1f), true);
        [Tooltip("Intensité de la teinte d'herbe sur le terrain lointain. 0 = aucune, 1 = pleine.")]
        public ClampedFloatParameter tintStrength = new ClampedFloatParameter(1f, 0f, 1f, true);
        [Tooltip("Brillance du sol une fois teinté en herbe.")]
        public ClampedFloatParameter tintSmoothness = new ClampedFloatParameter(0.25f, 0f, 1f, true);
        [Tooltip("Brillance : 0 = garde celle de la texture du terrain (les cailloux ressortent), 1 = brillance d'herbe uniforme (le détail de la texture est masqué).")]
        public ClampedFloatParameter smoothnessBlend = new ClampedFloatParameter(1f, 0f, 1f, true);
        [Tooltip("Relief : 0 = garde le relief de la texture du terrain (bosses des cailloux), 1 = sol lisse + vagues de vent seulement (le détail de la texture est gommé).")]
        public ClampedFloatParameter normalBlend = new ClampedFloatParameter(1f, 0f, 1f, true);
        [Tooltip("Distance à laquelle la teinte d'herbe commence à apparaître, en % de la distance de teinte pleine.")]
        public ClampedFloatParameter distanceStartPct = new ClampedFloatParameter(25f, 0f, 100f, true);
        [Tooltip("Distance (m) à laquelle la teinte d'herbe est pleine (distance de référence dont dérive le Start %).")]
        public MinFloatParameter distanceFull = new MinFloatParameter(120f, 1f, true);
        [Tooltip("Force de l'ondulation du relief par les vagues de vent (visible de loin / d'en haut).")]
        public ClampedFloatParameter waveNormalStrength = new ClampedFloatParameter(0.5f, 0f, 2f, true);
        [Tooltip("Force de l'ondulation de luminosité par les vagues de vent.")]
        public ClampedFloatParameter waveLumStrength = new ClampedFloatParameter(0.15f, 0f, 1f, true);

        [Header("Vent (partagé entre l'herbe et le sol teinté)")]
        [Tooltip("Direction du vent (X,Z) s'il n'y a pas de WindZone dans la scène (sinon = l'orientation du WindZone).")]
        public Vector2 windDirection = new Vector2(1f, 0.35f);
        [Tooltip("Force globale du vent. 0 = pas de vent (pas d'ondulation).")]
        [Min(0f)] public float windMain = 1f;
        [Tooltip("Petits frémissements rapides en plus des grandes vagues.")]
        [Range(0f, 2f)] public float windTurbulence = 0.3f;
        [Tooltip("Ampleur des rafales (creux et bosses des vagues).")]
        [Range(0f, 1f)] public float windPulseMagnitude = 0.5f;
        [Tooltip("Vitesse de défilement des rafales.")]
        [Range(0f, 3f)] public float windPulseFrequency = 0.4f;
        [Tooltip("0 = vagues organiques arrondies (taches), 1 = vagues en lignes nettes perpendiculaires au vent.")]
        [Range(0f, 1f)] public float windDirectionality = 0.7f;
        [Tooltip("0 = vagues qui défilent en bloc (peut donner une impression de texture qui glisse), 1 = vagues qui se déforment sur place. Milieu = rafales qui roulent en se déformant.")]
        [Range(0f, 1f)] public float windEvolve = 0.35f;

        public override string Keyword => "_GRASS_TINT";

        [System.NonSerialized] WindZone m_WindZone;
        WindZone ResolveWindZone()
        {
            if (m_WindZone == null) m_WindZone = Object.FindFirstObjectByType<WindZone>();
            return m_WindZone;
        }

        static readonly int ID_TintColor = Shader.PropertyToID("_GrassTintColor");
        static readonly int ID_TintStrength = Shader.PropertyToID("_GrassTintStrength");
        static readonly int ID_TintSmoothness = Shader.PropertyToID("_GrassTintSmoothness");
        static readonly int ID_SmoothnessBlend = Shader.PropertyToID("_GrassSmoothnessBlend");
        static readonly int ID_NormalBlend = Shader.PropertyToID("_GrassNormalBlend");
        static readonly int ID_DistanceStart = Shader.PropertyToID("_GrassTintDistanceStart");
        static readonly int ID_DistanceFull = Shader.PropertyToID("_GrassTintDistanceFull");
        static readonly int ID_WaveNormalStrength = Shader.PropertyToID("_GrassWaveNormalStrength");
        static readonly int ID_WaveLumStrength = Shader.PropertyToID("_GrassWaveLumStrength");
        static readonly int[] ID_EnableLayer = new int[8];

        // Wind globals (shared with the L0 blades in Phase 2+).
        static readonly int ID_WindDir = Shader.PropertyToID("_GrassWindDir");
        static readonly int ID_WindMain = Shader.PropertyToID("_GrassWindMain");
        static readonly int ID_WindTurbulence = Shader.PropertyToID("_GrassWindTurbulence");
        static readonly int ID_WindPulseMagnitude = Shader.PropertyToID("_GrassWindPulseMagnitude");
        static readonly int ID_WindPulseFrequency = Shader.PropertyToID("_GrassWindPulseFrequency");
        static readonly int ID_WindTime = Shader.PropertyToID("_GrassWindTime");
        static readonly int ID_WindDirectionality = Shader.PropertyToID("_GrassWindDirectionality");
        static readonly int ID_WindEvolve = Shader.PropertyToID("_GrassWindEvolve");

        static GrassTintModule()
        {
            for (int i = 0; i < 8; i++)
                ID_EnableLayer[i] = Shader.PropertyToID($"_EnableGrassTintLayer{i}");
        }

        public override void Apply(TerrainApplyContext ctx)
        {
            var m = ctx.material;
            // Push value-or-DEFAULT (defaults = the field-initializer values) so unchecking an override
            // reverts to the default instead of leaving the last-pushed material value stuck.
            Push(m, ID_TintColor, tintColor, new Color(0.25f, 0.4f, 0.15f, 1f), true); // linéaire
            Push(m, ID_TintStrength, tintStrength, 1f);
            Push(m, ID_TintSmoothness, tintSmoothness, 0.25f);
            Push(m, ID_SmoothnessBlend, smoothnessBlend, 1f);
            Push(m, ID_NormalBlend, normalBlend, 1f);
            // Tint Start dérive de Tint Full (%) — value-or-default sur chaque param (cohérent avec le
            // reste de l'Apply qui pousse value-or-default au matériau).
            float fullEff  = distanceFull.overrideState ? distanceFull.value : 120f;
            float startPct = distanceStartPct.overrideState ? distanceStartPct.value : 25f;
            m.SetFloat(ID_DistanceStart, fullEff * startPct * 0.01f);
            Push(m, ID_DistanceFull, distanceFull, 120f);
            Push(m, ID_WaveNormalStrength, waveNormalStrength, 0.5f);
            Push(m, ID_WaveLumStrength, waveLumStrength, 0.15f);

            // Layers: override OFF -> 0 (no tint layer) = disable the logic; ON -> the toggles.
            for (int i = 0; i < ctx.layerCount; i++)
                m.SetFloat(ID_EnableLayer[i], layers.overrideState && layers[i] ? 1f : 0f);

            // Shared wind field (GLOBALS, not per-material) — drives the L2 gust bands now and
            // the L0 blades after Phase 2. Pushed here so L2 animates with no extra scene component.
            Vector2 dir = windDirection;
            var wz = ResolveWindZone();
            if (wz != null)
            {
                Vector3 f = wz.transform.forward;
                Vector2 d = new Vector2(f.x, f.z);
                if (d.sqrMagnitude > 1e-6f) dir = d;
            }
            if (dir.sqrMagnitude < 1e-6f) dir = new Vector2(1f, 0f);
            dir.Normalize();
            Shader.SetGlobalVector(ID_WindDir, new Vector4(dir.x, dir.y, 0f, 0f));
            Shader.SetGlobalFloat(ID_WindMain, windMain);
            Shader.SetGlobalFloat(ID_WindTurbulence, windTurbulence);
            Shader.SetGlobalFloat(ID_WindPulseMagnitude, windPulseMagnitude);
            Shader.SetGlobalFloat(ID_WindPulseFrequency, windPulseFrequency);
            Shader.SetGlobalFloat(ID_WindDirectionality, windDirectionality);
            Shader.SetGlobalFloat(ID_WindEvolve, windEvolve);
        }

        // Time as a GLOBAL so the gust field advances each frame (edit mode: the controller
        // ticks with EditorTime + repaints the Scene view).
        public override void Tick(TerrainApplyContext ctx)
        {
            Shader.SetGlobalFloat(ID_WindTime, ctx.time);
        }
    }
}
