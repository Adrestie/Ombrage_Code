// OceanParameter.cs  (Ocean_v2 — architecture des paramètres à deux niveaux)
// Paramètre de module à OVERRIDE, calqué sur VolumeParameter<T> de HDRP.
//
// Deux niveaux d'activation dans le système :
//   • MODULE   (OceanFeatureModule.active) : concept ON/OFF. OFF = comme absent (rien poussé).
//   • VALEUR   (OceanParameter.overridden) : OFF = valeur par défaut du concept ; ON = valeur saisie.
//
// Contrat : un module lit TOUJOURS `param.Effective` (jamais `param.value` directement), de sorte
// qu'une valeur non surchargée retombe sur son défaut — défini UNE fois à la construction du champ
// (ex. `new OceanFloatParameter(0.06f)`), jamais éditable par l'utilisateur.
//
// Sérialisation Unity : les génériques ne sont pas sérialisables tels quels → une sous-classe
// CONCRÈTE [Serializable] par type (Float/Color/Bool/Vector3/Int). Le champ du module doit être
// déclaré du type concret. Le défaut vient de l'initialiseur inline du champ (préservé au chargement
// même si la donnée sérialisée ne le contient pas encore).
using System;
using UnityEngine;

namespace Ombrage.OceanFeatures
{
    /// Base NON générique : porte le drapeau d'override et sert de cible unique au PropertyDrawer
    /// (via [CustomPropertyDrawer(typeof(OceanParameterBase), true)]).
    [Serializable]
    public abstract class OceanParameterBase
    {
        [Tooltip("Surcharger cette valeur ? Décoché = valeur par défaut du concept.")]
        [SerializeField] public bool overridden = false;
    }

    /// Paramètre typé : valeur saisie + valeur par défaut. `Effective` est la seule lecture autorisée.
    [Serializable]
    public abstract class OceanParameter<T> : OceanParameterBase
    {
        [SerializeField] public T value;
        [SerializeField] public T defaultValue;

        /// Valeur réellement appliquée : la saisie si surchargée, sinon le défaut du concept.
        public T Effective => overridden ? value : defaultValue;

        protected OceanParameter(T def)
        {
            defaultValue = def;
            value = def;   // point de départ neutre quand l'utilisateur activera l'override
        }
    }

    [Serializable]
    public sealed class OceanFloatParameter : OceanParameter<float>
    {
        public OceanFloatParameter(float def) : base(def) { }
    }

    [Serializable]
    public sealed class OceanIntParameter : OceanParameter<int>
    {
        public OceanIntParameter(int def) : base(def) { }
    }

    [Serializable]
    public sealed class OceanBoolParameter : OceanParameter<bool>
    {
        public OceanBoolParameter(bool def) : base(def) { }
    }

    [Serializable]
    public sealed class OceanColorParameter : OceanParameter<Color>
    {
        public OceanColorParameter(Color def) : base(def) { }
    }

    [Serializable]
    public sealed class OceanVector3Parameter : OceanParameter<Vector3>
    {
        public OceanVector3Parameter(Vector3 def) : base(def) { }
    }
}
