// OceanRefractionModule.cs  (Ocean_v2)
// Module RÉFRACTION (see-through du fond) — pilote la lecture du fond opaque à travers l'eau dans la
// passe Forward de la surface. Le COMPOSITE lui-même vit dans le shader (OceanSurfaceData.hlsl) : on
// échantillonne le color pyramid (fond déjà rendu) à un UV distordu par la normale des vagues, et on
// mélange couleur d'eau / fond réfracté selon l'opacité de Beer-Lambert sur la LONGUEUR DE TRAJET 3D.
//
// Ce module ne fait que POUSSER les deux réglages de ce composite en globaux (SET pur via ctx.globals,
// anti-bug n°1). L'INTERRUPTEUR de consommation (_OceanRefractionEnabled) est poussé par le module de
// SURFACE (le consommateur), comme pour l'absorption/l'écume : un module désactivé n'Apply plus, donc
// il ne peut pas s'éteindre lui-même — c'est la surface, toujours active, qui rend l'effet inerte.
//
// Module absent OU désactivé ⇒ _OceanRefractionEnabled = 0 ⇒ la surface retombe sur l'eau OPAQUE
// colorée (alpha = _BaseColor.a), exactement comme avant l'introduction de la réfraction.
//
// Prérequis pipeline : le color pyramid n'existe que si « Rough Refraction » est actif (HDRP Asset +
// Frame Settings). Sans lui, SampleCameraColor renvoie du noir (documenté côté shader).
using UnityEngine;

namespace Ombrage.OceanFeatures
{
    [OceanModuleMenu("Rendering/Refraction")]
    public class OceanRefractionModule : OceanFeatureModule
    {
        // Valeurs à OVERRIDE (niveau 2, cf. Reflection/Absorption). Décoché = défaut validé ; cocher =
        // saisie d'une valeur différente. Clamp appliqué sur .value en OnValidate.
        [Header("Réfraction (see-through du fond)")]
        [Tooltip("Distance de clarté (m) : longueur de trajet de la lumière DANS l'eau au-delà de laquelle le fond n'est plus visible (eau opaque). Vu du dessus d'un haut-fond → trajet court → transparent ; vue rasante / fond lointain → trajet long → opaque. C'est une distance de TRAJET 3D, pas une profondeur verticale.")]
        public OceanFloatParameter clarityDistance = new OceanFloatParameter(6f);

        [Tooltip("Force de distorsion du fond par les vagues : décalage (en fraction d'écran) de l'échantillon du color pyramid selon la pente de la surface. 0 = fond net (pas d'ondulation) ; plus haut = fond plus ondulé. La distorsion s'annule quand l'eau devient opaque.")]
        public OceanFloatParameter distortionStrength = new OceanFloatParameter(0.03f);

        // ── Blend maître transparent ↔ fog sous-marin (bornes de remap de l'opacité) ──
        // L'opacité pilotée par la distance (0 près → 1 loin) est REMAPPÉE dans [fogMin, fogMax] :
        //   t = lerp(fogMin, fogMax, saturate(trajet / clarityDistance)).
        // fogMin = plancher (voile résiduel en eau claire) ; fogMax = plafond (fog au loin).
        [Header("Blend transparent ↔ fog sous-marin")]
        [Tooltip("Opacité de fog MINIMALE (plancher), en eau très peu profonde / trajet nul. 0 = eau 100% cristalline au plus clair ; monter garde toujours un voile de fog (l'eau n'est jamais totalement transparente).")]
        public OceanFloatParameter fogMin = new OceanFloatParameter(0f);

        [Tooltip("Opacité de fog MAXIMALE (plafond), à la distance de clarté et au-delà. 1 = fog plein opaque (le fond disparaît) ; baisser laisse toujours deviner le fond (l'eau ne devient jamais 100% opaque).")]
        public OceanFloatParameter fogMax = new OceanFloatParameter(1f);

        // Globaux consommés côté shader dans OceanSurfaceData.hlsl (HORS UnityPerMaterial).
        static readonly int ID_ClarityDist = Shader.PropertyToID("_OceanRefractionClarityDist");
        static readonly int ID_Distort      = Shader.PropertyToID("_OceanRefractionDistort");
        static readonly int ID_FogMin       = Shader.PropertyToID("_OceanRefractionFogMin");
        static readonly int ID_FogMax       = Shader.PropertyToID("_OceanRefractionFogMax");

        public override void Apply(OceanApplyContext ctx)
        {
            // SET pur non cumulatif (anti-bug n°1). L'interrupteur enable est poussé par la surface.
            ctx.globals.SetGlobalFloat(ID_ClarityDist, clarityDistance.Effective);
            ctx.globals.SetGlobalFloat(ID_Distort, distortionStrength.Effective);
            ctx.globals.SetGlobalFloat(ID_FogMin, fogMin.Effective);
            ctx.globals.SetGlobalFloat(ID_FogMax, fogMax.Effective);
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            clarityDistance.value = Mathf.Max(0.1f, clarityDistance.value);
            distortionStrength.value = Mathf.Clamp(distortionStrength.value, 0f, 0.5f);
            // Bornes de blend : plancher dans [0..1], plafond dans [plancher..1] (le fog au loin ≥ le voile
            // proche). Les deux extrêmes restent atteignables : (0,0)=transparent partout, (1,1)=fog partout.
            fogMin.value = Mathf.Clamp01(fogMin.value);
            fogMax.value = Mathf.Clamp(fogMax.value, fogMin.value, 1f);
        }
#endif
    }
}
