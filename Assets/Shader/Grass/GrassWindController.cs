// GrassWindController.cs
// ---------------------------------------------------------------------------------
// THE single source of grass wind. Pushes the _GrassWind* globals that both the
// L2 tinted terrain (TerrainLitCustomData.hlsl, now) and the L0 blades
// (GrassBRGVertex.hlsl, wired in Phase 2) read via GrassWind.hlsl.
//
// Drop one of these in the scene (any GameObject). Optionally point it at a WindZone
// for direction; strengths are set directly here. [ExecuteAlways] so the waves animate
// in the Scene view without entering Play mode.
//
// NOTE: while the grass blade system is still on the backup-04 code (which ALSO pushes
// these globals from its own WindZone), disable GrassBRGTest during L2 testing — or
// accept that the last writer each frame wins. Phase 2 makes the rebuilt blade system
// CONSUME these globals instead of pushing its own, ending the redundancy.
// ---------------------------------------------------------------------------------

using UnityEngine;

[ExecuteAlways]
public class GrassWindController : MonoBehaviour
{
    [Tooltip("Optional. If set, wind direction = this WindZone's forward (projected to XZ).")]
    public WindZone windZone;

    [Header("Direction (used when no WindZone is assigned)")]
    [Tooltip("World XZ wind direction. Normalized in-shader.")]
    public Vector2 directionXZ = new Vector2(1f, 0.35f);

    [Header("Strength")]
    [Tooltip("Overall wind strength. 0 = no wind => no waves.")]
    [Range(0f, 2f)] public float main = 1f;
    [Tooltip("Small-scale chaotic strength.")]
    [Range(0f, 2f)] public float turbulence = 0.3f;
    [Tooltip("Gust envelope amplitude (peaks).")]
    [Range(0f, 1f)] public float pulseMagnitude = 0.5f;
    [Tooltip("Gust travel speed.")]
    [Range(0f, 3f)] public float pulseFrequency = 0.4f;

    static readonly int ID_Dir       = Shader.PropertyToID("_GrassWindDir");
    static readonly int ID_Main      = Shader.PropertyToID("_GrassWindMain");
    static readonly int ID_Turb      = Shader.PropertyToID("_GrassWindTurbulence");
    static readonly int ID_PulseMag  = Shader.PropertyToID("_GrassWindPulseMagnitude");
    static readonly int ID_PulseFreq = Shader.PropertyToID("_GrassWindPulseFrequency");
    static readonly int ID_Time      = Shader.PropertyToID("_GrassWindTime");

    void OnEnable()   { Push(); }
    void OnValidate() { Push(); }

    void Update()
    {
        Push();
#if UNITY_EDITOR
        if (!Application.isPlaying)
            UnityEditor.SceneView.RepaintAll(); // animate the waves in the Scene view (edit mode)
#endif
    }

    void Push()
    {
        Vector2 dir = directionXZ;
        if (windZone != null)
        {
            Vector3 f = windZone.transform.forward;
            Vector2 d = new Vector2(f.x, f.z);
            if (d.sqrMagnitude > 1e-6f) dir = d;
        }
        if (dir.sqrMagnitude < 1e-6f) dir = new Vector2(1f, 0f);
        dir.Normalize();

        Shader.SetGlobalVector(ID_Dir, new Vector4(dir.x, dir.y, 0f, 0f));
        Shader.SetGlobalFloat(ID_Main, main);
        Shader.SetGlobalFloat(ID_Turb, turbulence);
        Shader.SetGlobalFloat(ID_PulseMag, pulseMagnitude);
        Shader.SetGlobalFloat(ID_PulseFreq, pulseFrequency);

        // Real-time clock so the field advances in edit mode too (Time.time is frozen there).
        float t = Application.isPlaying ? Time.time : Time.realtimeSinceStartup;
        Shader.SetGlobalFloat(ID_Time, t);
    }
}
