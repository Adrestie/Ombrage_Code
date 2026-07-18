// TerrainParameter.cs
// Paramètre façon VolumeParameter HDRP : une valeur + un overrideState.
// Sémantique "override = contrôle" : si overrideState est faux, le module ne pousse
// PAS la propriété matériau correspondante (la valeur sérialisée du .mat reste).
//
// IMPORTANT (sérialisation Unity) : un champ se sérialise selon son TYPE DÉCLARÉ.
// Pour conserver les bornes (min/max), un module doit déclarer le champ avec le type
// concret précis (ex. ClampedFloatParameter), pas le type de base FloatParameter.
using System;
using UnityEngine;

namespace Ombrage.TerrainFeatures
{
    /// Base non générique — sert à l'éditeur (réflexion, All/None).
    [Serializable]
    public abstract class TerrainParameter
    {
        [SerializeField] public bool overrideState;
        public abstract object GetBoxedValue();
    }

    [Serializable]
    public class TerrainParameter<T> : TerrainParameter
    {
        [SerializeField] public T value;

        public TerrainParameter() { }
        public TerrainParameter(T value, bool overrideState = false)
        {
            this.value = value;
            this.overrideState = overrideState;
        }

        public override object GetBoxedValue() => value;
        public static implicit operator T(TerrainParameter<T> p) => p.value;
    }

    // --- Types concrets simples ---
    [Serializable] public class BoolParameter      : TerrainParameter<bool>          { public BoolParameter() { } public BoolParameter(bool v, bool o = false) : base(v, o) { } }
    [Serializable] public class FloatParameter     : TerrainParameter<float>         { public FloatParameter() { } public FloatParameter(float v, bool o = false) : base(v, o) { } }
    [Serializable] public class IntParameter       : TerrainParameter<int>           { public IntParameter() { } public IntParameter(int v, bool o = false) : base(v, o) { } }
    [Serializable] public class ColorParameter     : TerrainParameter<Color>         { public ColorParameter() { } public ColorParameter(Color v, bool o = false) : base(v, o) { } }
    [Serializable] public class Vector2Parameter   : TerrainParameter<Vector2>       { public Vector2Parameter() { } public Vector2Parameter(Vector2 v, bool o = false) : base(v, o) { } }
    [Serializable] public class Texture2DParameter : TerrainParameter<Texture2D>     { public Texture2DParameter() { } public Texture2DParameter(Texture2D v, bool o = false) : base(v, o) { } }
    [Serializable] public class AnimationCurveParameter : TerrainParameter<AnimationCurve> { public AnimationCurveParameter() { } public AnimationCurveParameter(AnimationCurve v, bool o = false) : base(v, o) { } }

    // --- Types bornés (rendent un slider dans l'éditeur) ---
    [Serializable]
    public class ClampedFloatParameter : FloatParameter
    {
        public float min;
        public float max;
        public ClampedFloatParameter() { }
        public ClampedFloatParameter(float v, float min, float max, bool o = false) : base(v, o) { this.min = min; this.max = max; }
    }

    [Serializable]
    public class MinFloatParameter : FloatParameter
    {
        public float min;
        public MinFloatParameter() { }
        public MinFloatParameter(float v, float min, bool o = false) : base(v, o) { this.min = min; }
    }

    [Serializable]
    public class ClampedIntParameter : IntParameter
    {
        public int min;
        public int max;
        public ClampedIntParameter() { }
        public ClampedIntParameter(int v, int min, int max, bool o = false) : base(v, o) { this.min = min; this.max = max; }
    }

    /// Bascule par couche (8 bools, indices 0..7).
    [Serializable]
    public class LayerToggleParameter : TerrainParameter
    {
        [SerializeField] public bool[] value = new bool[8];

        public LayerToggleParameter() { }
        public LayerToggleParameter(bool[] v, bool o = false)
        {
            value = (v != null && v.Length == 8) ? v : new bool[8];
            overrideState = o;
        }

        public override object GetBoxedValue() => value;
        public bool this[int i] => value != null && i >= 0 && i < value.Length && value[i];
    }
}
