// WindModule.cs
// Feature Wind Displacement (keyword _WIND_DISPLACEMENT), PILOTÉ PAR UN WindZone.
// L'override expose des FACTEURS (×) des variables du WindZone (windMain/turbulence/
// pulseMagnitude/pulseFrequency) ; case décochée = facteur neutre 1 (valeur WindZone telle quelle).
// Direction = forward du GameObject du WindZone (XZ) + vecteur d'offset de l'override.
//
// Mapping « Main = ampleur + vitesse » (choix user) vers le modèle shader (noise scroll) :
//   amplitude (_WindMaxValue) = (effMain + effPulseMag) × amplitudeScale ; _WindMinValue = 0
//   défilement global         = dir × effMain × scrollSpeed
//   défilement detail         = dir × (effMain + effTurb) × scrollSpeed   (turbulence = flutter fin)
//   période (_WindPeriod)     = effPulseFreq
// Le WindZone est une ref de SCÈNE → portée par le contrôleur (TerrainProfileController.windZone).
// GATÉ : keyword ON seulement si un module active le displacement de vertex (Tessellation).
using UnityEngine;

namespace Ombrage.TerrainFeatures
{
    public enum WindDebugMode { Off = 0, GlobalOnly = 1, DetailOnly = 2, Combined = 3, Displacement = 4 }

    [TerrainModuleMenu("Displacement/Wind Displacement")]
    public class WindModule : TerrainFeatureModule
    {
        [Header("Facteurs WindZone (× ; off = 1 = valeur WindZone)")]
        [Tooltip("Facteur sur windMain. Pilote l'amplitude ET la vitesse de défilement.")]
        public MinFloatParameter windMain = new MinFloatParameter(1f, 0f);
        [Tooltip("Facteur sur windTurbulence. Pilote la vitesse du detail noise (flutter fin).")]
        public MinFloatParameter windTurbulence = new MinFloatParameter(1f, 0f);
        [Tooltip("Facteur sur windPulseMagnitude. Ajoute de l'amplitude (profondeur des rafales).")]
        public MinFloatParameter windPulseMagnitude = new MinFloatParameter(1f, 0f);
        [Tooltip("Facteur sur windPulseFrequency. Pilote la période de pulsation.")]
        public MinFloatParameter windPulseFrequency = new MinFloatParameter(1f, 0f);
        [Tooltip("Offset (monde XZ) ajouté au forward du WindZone pour la direction de défilement.")]
        public Vector2Parameter directionOffset = new Vector2Parameter(Vector2.zero);

        [Header("Conversion (unités shader)")]
        [Tooltip("Mètres de _WindMaxValue par unité de windMain effectif.")]
        [Min(0f)] public float amplitudeScale = 0.1f;
        [Tooltip("Vitesse de défilement (unités monde/sec) par unité de windMain effectif.")]
        [Min(0f)] public float scrollSpeed = 1f;

        [Header("Noise")]
        public Texture2D globalMap;
        public Texture2D detailMap;
        [Range(0.001f, 0.5f)] public float globalTile = 0.01f;
        [Range(0.001f, 1f)] public float detailTile = 0.05f;

        [Header("Debug")]
        public WindDebugMode debugMode = WindDebugMode.Off;

        public override string Keyword => "_WIND_DISPLACEMENT";
        public override bool KeywordEnabled(TerrainApplyContext ctx) => active && ctx.VertexDisplacementActive();
        public override float GetMaxVertexDisplacement() => active ? m_CurMaxAmplitude : 0f;

        [System.NonSerialized] float m_CurMaxAmplitude;
        [System.NonSerialized] WindZone m_WindZone;

        static readonly int ID_GlobalMap = Shader.PropertyToID("_WindGlobalMap");
        static readonly int ID_DetailMap = Shader.PropertyToID("_WindDetailMap");
        static readonly int ID_GlobalTile = Shader.PropertyToID("_WindGlobalTile");
        static readonly int ID_DetailTile = Shader.PropertyToID("_WindDetailTile");
        static readonly int ID_MinValue = Shader.PropertyToID("_WindMinValue");
        static readonly int ID_MaxValue = Shader.PropertyToID("_WindMaxValue");
        static readonly int ID_GlobalOffsetDir = Shader.PropertyToID("_WindGlobalOffsetDir");
        static readonly int ID_DetailOffsetDir = Shader.PropertyToID("_WindDetailOffsetDir");
        static readonly int ID_Period = Shader.PropertyToID("_WindPeriod");
        static readonly int ID_Time = Shader.PropertyToID("_WindTime");
        static readonly int ID_DebugMode = Shader.PropertyToID("_WindDebugMode");

        static float Factor(MinFloatParameter p) => p.overrideState ? p.value : 1f;

        // Auto-résolution du WindZone de la scène (mise en cache ; re-cherche si détruit).
        // Un SO ne peut pas sérialiser de ref de scène → on résout au runtime.
        WindZone ResolveWindZone()
        {
            if (m_WindZone == null) m_WindZone = Object.FindFirstObjectByType<WindZone>();
            return m_WindZone;
        }

        public override void Apply(TerrainApplyContext ctx)
        {
            var m = ctx.material;
            var wz = ResolveWindZone();

            float effMain = (wz != null ? wz.windMain : 0f) * Factor(windMain);
            float effTurb = (wz != null ? wz.windTurbulence : 0f) * Factor(windTurbulence);
            float effPulseMag = (wz != null ? wz.windPulseMagnitude : 0f) * Factor(windPulseMagnitude);
            float effPulseFreq = (wz != null ? wz.windPulseFrequency : 0f) * Factor(windPulseFrequency);

            // Direction = forward.xz (+ offset override), normalisée.
            Vector3 fwd = wz != null ? wz.transform.forward : Vector3.forward;
            Vector2 dir = new Vector2(fwd.x, fwd.z);
            if (directionOffset.overrideState) dir += directionOffset.value;
            if (dir.sqrMagnitude < 1e-6f) dir = new Vector2(1f, 0f);
            dir.Normalize();

            float maxAmp = (effMain + effPulseMag) * amplitudeScale;
            m_CurMaxAmplitude = maxAmp;

            m.SetFloat(ID_MinValue, 0f);
            m.SetFloat(ID_MaxValue, maxAmp);

            Vector2 globalScroll = dir * (effMain * scrollSpeed);
            Vector2 detailScroll = dir * ((effMain + effTurb) * scrollSpeed);
            m.SetVector(ID_GlobalOffsetDir, new Vector4(globalScroll.x, globalScroll.y, 0f, 0f));
            m.SetVector(ID_DetailOffsetDir, new Vector4(detailScroll.x, detailScroll.y, 0f, 0f));

            m.SetFloat(ID_Period, effPulseFreq);
            m.SetFloat(ID_GlobalTile, globalTile);
            m.SetFloat(ID_DetailTile, detailTile);
            if (globalMap != null) m.SetTexture(ID_GlobalMap, globalMap);
            if (detailMap != null) m.SetTexture(ID_DetailMap, detailMap);
            m.SetFloat(ID_DebugMode, (float)(int)debugMode);
        }

        public override void Tick(TerrainApplyContext ctx)
        {
            ctx.material.SetFloat(ID_Time, ctx.time);
        }
    }
}
