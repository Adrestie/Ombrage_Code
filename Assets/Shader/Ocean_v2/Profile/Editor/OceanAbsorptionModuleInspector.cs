// OceanAbsorptionModuleInspector.cs  (Ocean_v2 — éditeur)
// Extras d'inspecteur du module Absorption, appelés par OceanProfileEditor (qui dessine les modules
// génériquement) : (1) WARNING non-bloquant si l'ordre d'absorption est non-physique ; (2) PRESETS Jerlov
// (boutons « couleur réaliste ») qui remplissent waterColor depuis les ancres (la visibilité vit dans Underwater).
using UnityEditor;
using UnityEngine;

namespace Ombrage.OceanFeatures
{
    static class OceanAbsorptionModuleInspector
    {
        public static void DrawExtras(OceanAbsorptionModule m, SerializedObject mso)
        {
            EditorGUILayout.Space(2);

            // ── Warning non-physique (info, non bloquant) ──
            // L'eau réelle absorbe le rouge en premier → σ_r ≥ σ_g ≥ σ_b. Si l'ordre choisi le viole
            // (couleur/absorptionColor tordue), on le signale sans l'interdire (fonctionnalité assumée).
            Vector3 s = OceanAbsorptionModule.DeriveSigma(
                m.waterColor, m.absorptionColor.overridden, m.absorptionColor.value);
            if (!(s.x >= s.y && s.y >= s.z))
                EditorGUILayout.HelpBox(
                    "Ordre d'absorption non-physique (l'eau réelle absorbe le rouge en premier : σ_r ≥ σ_g ≥ σ_b). " +
                    "OK en stylisé, mais rendu artificiel.", MessageType.Info);

            // ── Presets Jerlov (couleur réaliste de départ) ──
            EditorGUILayout.LabelField("Presets Jerlov (couleur réaliste)", EditorStyles.miniBoldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                DrawPreset(mso, m.anchorIa,  "Ia — claire");
                DrawPreset(mso, m.anchorII,  "II — côtier");
                DrawPreset(mso, m.anchorIII, "III — vert");
            }
        }

        static void DrawPreset(SerializedObject mso, WaterAbsorptionProfile anchor, string label)
        {
            using (new EditorGUI.DisabledScope(anchor == null))
            {
                if (GUILayout.Button(anchor != null ? label : label + " (?)"))
                    ApplyPreset(mso, anchor);
            }
        }

        // Écrit via le SerializedObject (le profil appelle ApplyModifiedProperties ensuite → persiste + Undo).
        // Preset = couleur réaliste convertie depuis le σ de l'ancre + ordre physique (absorptionColor décoché).
        // La visibilité (distance de vue) n'est PAS touchée : elle vit dans le module Underwater.
        static void ApplyPreset(SerializedObject mso, WaterAbsorptionProfile anchor)
        {
            if (anchor == null) return;
            mso.FindProperty("waterColor").colorValue = OceanAbsorptionModule.LookFromSigma(anchor.Sigma);
            // Ordre d'absorption = physique (on ne tord pas) → override décoché.
            mso.FindProperty("absorptionColor").FindPropertyRelative("overridden").boolValue = false;
            GUI.changed = true;
        }
    }
}
