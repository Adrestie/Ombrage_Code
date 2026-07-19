// OceanParameterDrawer.cs  (Ocean_v2 / P0)
// Dessine un OceanParameter dans l'inspecteur = interrupteur d'override + champ de valeur.
//   • Override décoché → le champ affiche le DÉFAUT du concept, grisé (non éditable).
//   • Override coché   → le champ affiche/édite la valeur saisie.
// S'applique à toutes les sous-classes concrètes via useForChildren:true. Pris en charge
// automatiquement par PropertyField (aucune modification de OceanProfileEditor nécessaire).
using UnityEditor;
using UnityEngine;

namespace Ombrage.OceanFeatures
{
    [CustomPropertyDrawer(typeof(OceanParameterBase), true)]
    public class OceanParameterDrawer : PropertyDrawer
    {
        const float kToggleWidth = 16f;
        const float kGap = 4f;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var overridden = property.FindPropertyRelative("overridden");
            var value      = property.FindPropertyRelative("value");
            var def        = property.FindPropertyRelative("defaultValue");

            EditorGUI.BeginProperty(position, label, property);

            // Interrupteur d'override, calé à gauche (aligné sur la marge, comme HDRP).
            var togRect = new Rect(position.x, position.y, kToggleWidth, EditorGUIUtility.singleLineHeight);
            overridden.boolValue = EditorGUI.Toggle(togRect, overridden.boolValue);

            // Champ de valeur, décalé après l'interrupteur. Affiche la valeur si surchargée, sinon
            // le défaut (grisé) — « désactivé = comme sa valeur par défaut ».
            var fieldRect = new Rect(
                position.x + kToggleWidth + kGap,
                position.y,
                position.width - kToggleWidth - kGap,
                position.height);

            if (overridden.boolValue)
            {
                EditorGUI.PropertyField(fieldRect, value, label, true);
            }
            else
            {
                using (new EditorGUI.DisabledScope(true))
                    EditorGUI.PropertyField(fieldRect, def, label, true);
            }

            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            var overridden = property.FindPropertyRelative("overridden");
            var shown = overridden.boolValue
                ? property.FindPropertyRelative("value")
                : property.FindPropertyRelative("defaultValue");
            return EditorGUI.GetPropertyHeight(shown, label, true);
        }
    }
}
