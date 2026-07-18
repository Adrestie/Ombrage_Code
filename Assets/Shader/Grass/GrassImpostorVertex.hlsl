// GrassImpostorVertex.hlsl
// ---------------------------------------------------------------------------------
// Horizon impostor — billboard vertex hook (Step 2). Mirrors the blade's
// ApplyMeshModification() contract (called by VertMesh in EVERY pass, BEFORE the
// object->world transform), but instead of a Bézier blade it orients a single quad to
// FACE THE CAMERA (cylindrical billboard: stays upright, rotates only about Y).
//
// Template quad carries parametric coords + the texture UV (baked in the mesh):
//   positionOS.x = side in [-0.5, 0.5]  (across the card width)
//   positionOS.y = v    in [ 0.0, 1.0]  (root -> top)
//   texCoord0    = (side+0.5, v)        (the impostor texture UV; passed through untouched)
//
// Placement: the card ROOT is the translation of the per-draw object matrix (a DrawMesh
// Translate in the Step 2 test, a per-instance matrix in the Step 3 BRG). We read that root
// in CAMERA-RELATIVE space straight from GetObjectToWorldMatrix(), so the billboard is robust
// to however HDRP set up camera-relative rendering (this is what the absolute-position +
// shared-identity trick got WRONG for Graphics.DrawMesh). The matrix is translation-only, so
// the object-space billboard offset we emit passes through unrotated.
//
// Included per pass AFTER Lit*Pass.hlsl (defines AttributesMesh) and BEFORE the
// ShaderPass*.hlsl driver (which calls ApplyMeshModification), exactly like the blade.
// ---------------------------------------------------------------------------------

#ifndef GRASS_IMPOSTOR_VERTEX_INCLUDED
#define GRASS_IMPOSTOR_VERTEX_INCLUDED

// Card size fallback (m) used when the per-instance height/width are 0 (material preview / test draws
// that only set the size on some cards). Per-instance values (_GrassParams.x / _GrassParams2.x) win.
float _GrassImpostorCardWidth;
float _GrassImpostorCardHeight;
// GAME / cull camera world position (absolute), pushed by the runtime (the SAME global the blades use). The
// card faces THIS, and the surface fades by the distance to it -> everything is Game-camera-relative, so the
// cards behave identically whether rendered from the Game view or inspected from the Scene view.
float3 _GrassCullCamPos;

AttributesMesh ApplyMeshModification(AttributesMesh input, float3 timeParameters)
{
    float side = input.positionOS.x;            // -0.5..0.5 across the card width
    float vv   = saturate(input.positionOS.y);  //  0..1 root -> top

    float height = (_GrassParams.x  > 1e-4) ? _GrassParams.x  : _GrassImpostorCardHeight;
    float width  = (_GrassParams2.x > 1e-4) ? _GrassParams2.x : _GrassImpostorCardWidth;

    // Card root in CAMERA-RELATIVE world space (the camera sits at the origin in RWS). Read from the actual
    // object matrix HDRP uses for THIS draw -> correct whatever the camera-relative path (DrawMesh / BRG).
    float4x4 o2w   = GetObjectToWorldMatrix();
    float3 cardRWS = float3(o2w._m03, o2w._m13, o2w._m23);

    // Cylindrical billboard: stay upright (world up), rotate about Y to face the GAME camera (_GrassCullCamPos,
    // absolute) — NOT the rendering camera — so from the Scene view the cards behave as the Game camera sees
    // them (matches the blades + the Game-camera-relative placement/cull/fade).
    float3 cardAbs = GetAbsolutePositionWS(cardRWS);
    float3 toCam   = _GrassCullCamPos - cardAbs;
    toCam.y = 0.0;                                                 // horizontal only -> grass stays vertical
    float  l = length(toCam);
    float3 fwd   = (l > 1e-4) ? toCam / l : float3(0.0, 0.0, 1.0); // facing dir = the card's normal
    float3 up    = float3(0.0, 1.0, 0.0);
    float3 right = normalize(cross(up, fwd));                       // card width axis (horizontal)

    // Object-space billboard offset. The matrix is translation-only (identity rotation), so HDRP's transform
    // gives positionRWS = offset + cardRWS = the billboard corner (no double camera offset, no absolute trick).
    input.positionOS = right * (side * width) + up * (vv * height);

#ifdef ATTRIBUTES_NEED_NORMAL
    input.normalOS = fwd;      // billboard faces the camera; the surface lifts it toward up for soft grass
#endif
    return input;
}

#endif // GRASS_IMPOSTOR_VERTEX_INCLUDED
