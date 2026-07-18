// GrassBRGVertex.hlsl
// ---------------------------------------------------------------------------------
// PHASE 1c.1 — Procedural Bézier blade, built in the HDRP vertex hook.
//
// ApplyMeshModification() is called by VertMesh() (when HAVE_MESH_MODIFICATION is defined)
// in EVERY pass — GBuffer, DepthOnly, ShadowCaster, MotionVectors — BEFORE the
// object->world transform. So a single implementation reshapes the geometry consistently
// across all passes (no per-pass duplication like the legacy DrawProcedural shader needed).
//
// Input template mesh carries only parametric coordinates:
//   positionOS.x = side  in [-0.5, 0.5]  (across the blade width)
//   positionOS.y = v     in [ 0.0, 1.0]  (along the blade, base -> tip)
//
// The blade is built in OBJECT space in real meters. unity_ObjectToWorld is a pure
// translation+yaw (NO non-uniform scale), so normals stay correct and bend/tilt are
// naturally proportional to height.
//
// 1c.1 keeps per-blade variation deterministic from world position (a hash) — no extra
// per-instance data yet. 1c.2 swaps that for the compute-fed DOTS properties; 1c.3 adds
// time-based wind (which the MV pass will replay with _LastTimeParameters automatically).
//
// Included per pass AFTER Lit*Pass.hlsl (which defines AttributesMesh) and BEFORE the
// ShaderPass*.hlsl driver (which includes VertMesh.hlsl and calls ApplyMeshModification).
// ---------------------------------------------------------------------------------

#ifndef GRASS_BRG_VERTEX_INCLUDED
#define GRASS_BRG_VERTEX_INCLUDED

// Étape B — the blades read the SAME wind field as the L2 tinted terrain (GrassGustSignal), so the
// gust rolling across the ground from altitude is exactly the one bending the blades up close
// (motion-matched -> prerequisite for the Phase 4 L0<->L2 crossfade). GrassWind.hlsl declares ALL the
// shared _GrassWind* globals (dir/main/turbulence/pulse/time/directionality/evolve) + the noise — so
// we must NOT redeclare them here.
#include "Assets/Shader/Grass/GrassWind.hlsl"

// Blade WIDTH: global (same for all blades; set via Shader.SetGlobalFloat). Height/bend/tilt/
// phase + width are PER-BLADE, read from the DOTS-instanced _GrassParams/_GrassParams2 (compute-fed).
// LOD width clamp (global, 0 = off): far blades widen with distance so they stay ~1 px on
// screen instead of dissolving into sub-pixel scintillation.
float _GrassLODWidthClamp;
// Width-clamp distance ramp [_GrassWidthClampStart, _GrassWidthClampEnd (= max blade distance)] + a
// baked curve (_GrassWidthCurve, x = 0 at the near end of the ramp -> 1 at the max distance) that
// shapes how the clamp fades in toward the far end. Independent of the collapse / band split.
float _GrassWidthClampStart;
float _GrassWidthClampEnd;
TEXTURE2D(_GrassWidthCurve);
SAMPLER(sampler_GrassWidthCurve);
// 1a vertex collapse (replaces the NEAR/FAR mesh swap): over [_GrassCollapseStart, _GrassCollapseEnd]
// the blade's row v snaps toward a _GrassCollapseTargetSeg-resolution lattice, so adjacent rows
// coincide (degenerate, rasterizer-culled) -> continuous detail reduction, no pop. Start>=End or
// TargetSeg==0 disables it. Pushed by GrassBladesModule.
float _GrassCollapseStart;
float _GrassCollapseEnd;
float _GrassCollapseTargetSeg;
// Maps the shared gust signal (~[-1.8,1.8]·windMain) to a blade lean angle (radians-ish). Blade-only
// tuning (the L2 tilts its normal by the same signal but has no geometry to bend).
float _GrassWindBendGain;
// CULL camera world position (absolute), pushed by the runtime. The melt/LOD distance MUST use this (not
// the render camera) so it matches the cull: a blade kept by the cull (d < maxBladeDistance from the cull
// cam) then always has height > 0 -> never a degenerate (height=0) Bézier -> no NaN normal. With
// cullFromMainCamera, the render cam (Scene view) differs from the cull cam (Main) — using the render
// distance there would melt kept blades to 0 and emit NaN normals (black screen). 0 vector = not set yet.
float3 _GrassCullCamPos;

// Deformation: the SHARED toroidal deformation RT (R = press amount), pushed as globals by the terrain's
// DeformationModule (Shader.SetGlobalTexture/Float). Blades flatten where the ground is pressed
// (vehicles/player). _GrassDeformInfluence (blade-only) = how hard the press lays them over; 0 = off.
TEXTURE2D(_DeformationMap);
SAMPLER(sampler_DeformationMap);
float _BufferWorldSize;
float _GrassDeformInfluence;

// ---- Cubic Bézier ----
float3 Grass_Bezier(float3 p0, float3 p1, float3 p2, float3 p3, float t)
{
    float u = 1.0 - t;
    return u*u*u * p0 + 3.0*u*u*t * p1 + 3.0*u*t*t * p2 + t*t*t * p3;
}

float3 Grass_BezierTangent(float3 p0, float3 p1, float3 p2, float3 p3, float t)
{
    float u = 1.0 - t;
    return 3.0*u*u * (p1 - p0) + 6.0*u*t * (p2 - p1) + 3.0*t*t * (p3 - p2);
}

// Yaw rotation about Y, matching the scatter's o2w (columns o0=(c,0,-s),o1=(0,1,0),o2=(s,0,c)).
float3 Grass_RotY(float3 v, float c, float s)    { return float3(c * v.x + s * v.z, v.y, -s * v.x + c * v.z); } // obj->world
float3 Grass_InvRotY(float3 v, float c, float s) { return float3(c * v.x - s * v.z, v.y,  s * v.x + c * v.z); } // world->obj

AttributesMesh ApplyMeshModification(AttributesMesh input, float3 timeParameters)
{
    // Parametric coords carried by the template mesh.
    float v    = saturate(input.positionOS.y);
    float side = input.positionOS.x;

    // Compression B: the transform is rebuilt from _GrassXform (abs world pos + yaw); unity_ObjectToWorld
    // is a shared identity. The blade is built in object space then rotated by yaw and placed at the
    // (camera-relative) world position. seed = absolute world XZ (wind/deform spatial coherence).
    float4 xf   = _GrassXform;
    float3 basePos = xf.xyz;          // ABSOLUTE world position
    float  yaw  = xf.w;
    float  cy, sy; sincos(yaw, sy, cy);
    float3 seed = basePos;

    // Per-blade shape from the DOTS-instanced property.
    float4 gp     = _GrassParams;
    float  height = gp.x;             // meters
    float  bend   = gp.y;             // forward curl (object +z)
    float  tilt   = gp.z;             // sideways lean (object +x)
    float  phase  = gp.w * 6.2831853; // wind decorrelation phase

    // Melt/LOD distance = distance from the CULL camera (matches the cull's `d`), NOT the render camera.
    // This guarantees a cull-kept blade (d < maxBladeDistance) has camDist < crossfadeEnd -> height > 0
    // (no degenerate Bézier -> no NaN), even when cullFromMainCamera makes render cam != cull cam.
    float camDist = distance(basePos, _GrassCullCamPos);

    // Phase 4 — L0<->L2 crossfade: the blade MELTS (height -> 0, sinking into the ground) across the
    // shared band as the L2 tint rises in its place, so the hand-off shows no edge/pop. The compute
    // hard-culls beyond _GrassCrossfadeEnd (= maxBladeDistance), so height never reaches exactly 0.
    if (_GrassCrossfadeEnd > _GrassCrossfadeStart)
        height *= 1.0 - smoothstep(_GrassCrossfadeStart, _GrassCrossfadeEnd, camDist);

    // ---- Shared wind: downwind axis (object space) + a signed sway scalar from the SHARED gust field
    // (motion-match with the L2 tint). timeParameters.x is the current time in normal passes and the
    // previous-frame time when the MotionVectors pass replays this hook -> correct object MV. ----
    float3 windOS = float3(0.0, 0.0, 0.0);
    float  a = 0.0; // sway angle (signed)
    if (_GrassWindMain > 0.0)
    {
        // World->object = inverse yaw rotation (rebuilt from _GrassXform.w).
        windOS = Grass_InvRotY(GrassWindDirWS(), cy, sy);
        windOS.y = 0.0;
        windOS = normalize(windOS + float3(1e-4, 0.0, 0.0));
        float tt      = _GrassWindTime - (_TimeParameters.x - timeParameters.x);
        float signal  = GrassGustSignal(seed.xz, tt);             // identical to the L2 terrain tilt
        float flutter = sin(tt * (2.0 + _GrassWindPulseFrequency) + phase) * 0.12 * _GrassWindMain;
        a = (signal + flutter) * _GrassWindBendGain;
    }

    // ---- Deformation: the blade is pressed flat where the ground is deformed (vehicles/player). The
    // RT is amount-only, so the lay-over direction = -gradient of the field (grass splays OUT of the
    // press). defA is a strong lean angle (0..~1.4 rad) toward defDirOS, scaled by the press amount. ----
    float3 defDirOS = float3(0.0, 0.0, 0.0);
    float  defA = 0.0;
    if (_GrassDeformInfluence > 0.0)
    {
        float  bws = max(_BufferWorldSize, 1e-3);
        float2 dUV = seed.xz / bws;                               // toroidal (RT wrap = Repeat)
        float  dC  = SAMPLE_TEXTURE2D_LOD(_DeformationMap, sampler_DeformationMap, dUV, 0).r;
        if (dC > 0.001)
        {
            const float e = 1.0 / 1024.0;                         // ~1 texel (default RT res)
            float dR = SAMPLE_TEXTURE2D_LOD(_DeformationMap, sampler_DeformationMap, dUV + float2(e, 0), 0).r;
            float dL = SAMPLE_TEXTURE2D_LOD(_DeformationMap, sampler_DeformationMap, dUV - float2(e, 0), 0).r;
            float dU = SAMPLE_TEXTURE2D_LOD(_DeformationMap, sampler_DeformationMap, dUV + float2(0, e), 0).r;
            float dD = SAMPLE_TEXTURE2D_LOD(_DeformationMap, sampler_DeformationMap, dUV - float2(0, e), 0).r;
            float2 g = float2(dR - dL, dU - dD);
            float2 dirWS = (dot(g, g) > 1e-8) ? -normalize(g) : float2(1.0, 0.0); // away from the press
            defDirOS = Grass_InvRotY(float3(dirWS.x, 0.0, dirWS.y), cy, sy);
            defDirOS.y = 0.0; defDirOS = normalize(defDirOS + float3(1e-4, 0.0, 0.0));
            defA = saturate(dC * _GrassDeformInfluence) * 1.4;    // up to ~80° lay-over
        }
    }

    // ---- MESH species (flower / tuft): keep the AUTHORED geometry, scale by height, sway by wind,
    // press by deformation. No Bézier reshape (that's only for the parametric blade strip). ----
    if (_GrassParams2.y >= 0.5)
    {
        float3 p = input.positionOS * height;   // height = per-instance scale (melted by the crossfade)
        p += windOS  * (p.y * a);               // shear: taller verts lean downwind with the gust
        p += defDirOS * (p.y * defA);           // ...and lay over where pressed
        // Rotate by yaw to world, place at the ABSOLUTE world position. unity_ObjectToWorld is a shared
        // identity, but HDRP's matrix accessor still subtracts the camera pos -> the final RWS is correct.
        input.positionOS = basePos + Grass_RotY(p, cy, sy);
#ifdef ATTRIBUTES_NEED_NORMAL
        input.normalOS = Grass_RotY(input.normalOS, cy, sy); // authored mesh normal -> world
#endif
        return input;
    }

    // ---- BLADE species: procedural Bézier blade (object space, real meters) ----
    float3 P0 = float3(0.0,                0.0,          0.0);
    float3 P1 = float3(tilt * 0.20 * height, 0.33 * height, bend * 0.20 * height);
    float3 P2 = float3(tilt * 0.50 * height, 0.66 * height, bend * 0.55 * height);
    float3 P3 = float3(tilt * 0.30 * height, 1.00 * height, bend * 1.00 * height);

    // Wind: bend the upper control points toward the wind (windOS/a are 0 when wind is off -> no-op).
    P1   += windOS * height * sin(a * 0.15);
    P2   += windOS * height * sin(a * 0.50);
    P2.y -= height * (1.0 - cos(a * 0.50)) * 0.3;
    P3   += windOS * height * sin(a);
    P3.y -= height * (1.0 - cos(a)) * 0.5;

    // Deformation: lay the blade over toward defDirOS (defDirOS/defA are 0 when undeformed -> no-op).
    P1   += defDirOS * height * sin(defA * 0.3);
    P2   += defDirOS * height * sin(defA * 0.6);
    P2.y -= height * (1.0 - cos(defA * 0.6)) * 0.5;
    P3   += defDirOS * height * sin(defA);
    P3.y -= height * (1.0 - cos(defA)) * 0.7;

    // 1a — vertex collapse: snap v toward a coarser lattice with distance so adjacent rows coincide
    // (degenerate quads, culled at raster) -> continuous detail reduction, replacing the NEAR/FAR mesh
    // swap. Blade branch only (mesh species kept their authored geometry above). floor(x+0.5) =
    // deterministic round (HLSL round() tie-breaking varies across GPUs).
    if (_GrassCollapseEnd > _GrassCollapseStart && _GrassCollapseTargetSeg > 0.5)
    {
        float collapse01 = saturate((camDist - _GrassCollapseStart) / (_GrassCollapseEnd - _GrassCollapseStart));
        float vCoarse = floor(v * _GrassCollapseTargetSeg + 0.5) / _GrassCollapseTargetSeg;
        v = lerp(v, vCoarse, collapse01);
    }

    float3 spine   = Grass_Bezier(P0, P1, P2, P3, v);
    float3 tangent = Grass_BezierTangent(P0, P1, P2, P3, max(v, 1e-3));
    // Guard: a fully-melted blade (height ~0) collapses all control points -> tangent 0 -> normalize = NaN
    // -> NaN GBuffer normal -> post-process spreads it -> black screen. Fall back to "up" when degenerate.
    float tlen = length(tangent);
    tangent = (tlen > 1e-5) ? tangent / tlen : float3(0.0, 1.0, 0.0);

    // Width tapers to a point at the tip; offset along object +x. Per-instance NATURAL width
    // (_GrassParams2.x, species-driven). FAR blades widen toward a screen-constant size so they don't
    // shrink to sub-pixel and flicker. The widening ramps in from _GrassWidthClampStart (near end, 0%)
    // to _GrassWidthClampEnd (= max blade distance, 100%), and the baked _GrassWidthCurve shapes the
    // amount across that ramp (x = 0..1). NEAR blades (before the ramp) keep their natural width exactly.
    float natW = _GrassParams2.x;
    float wT = (_GrassWidthClampEnd > _GrassWidthClampStart)
        ? saturate((camDist - _GrassWidthClampStart) / (_GrassWidthClampEnd - _GrassWidthClampStart)) : 0.0;
    float wCurve = SAMPLE_TEXTURE2D_LOD(_GrassWidthCurve, sampler_GrassWidthCurve, float2(wT, 0.5), 0).r;
    float baseWidth = max(natW, lerp(natW, camDist * _GrassLODWidthClamp, wCurve));
    float width = baseWidth * (1.0 - v);
    float3 pos = spine + float3(side * width, 0.0, 0.0);

    // Rotate the object-space blade by yaw to world, place at the ABSOLUTE world position (HDRP's matrix
    // accessor subtracts the camera pos from the shared-identity unity_ObjectToWorld -> correct RWS).
    input.positionOS = basePos + Grass_RotY(pos, cy, sy);

#ifdef ATTRIBUTES_NEED_NORMAL
    // Surface normal = perpendicular to (tangent, width direction). Face object +z so the
    // "front" side is lit consistently; the double-sided flip in the surface handles backfaces.
    float3 widthDir = float3(1.0, 0.0, 0.0);
    float3 nraw = cross(tangent, widthDir);
    float  nlen = length(nraw);
    float3 nrm = (nlen > 1e-5) ? nraw / nlen : float3(0.0, 0.0, 1.0); // guard normalize(0) -> NaN
    if (nrm.z < 0.0) nrm = -nrm;
    // unity_WorldToObject is a shared IDENTITY -> emit the normal in WORLD space (rotate by yaw);
    // HDRP's identity normal transform then passes it through unchanged.
    input.normalOS = Grass_RotY(nrm, cy, sy);
#endif

    return input;
}

#endif // GRASS_BRG_VERTEX_INCLUDED
